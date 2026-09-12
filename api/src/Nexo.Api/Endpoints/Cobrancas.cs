using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexo.Api.Cobranca;
using Nexo.Api.Dados;
using Nexo.Api.Dominio;
using Npgsql;

namespace Nexo.Api.Endpoints;

/// <summary>
/// A cobrança pelo PSP: emitir, e receber de volta a notícia do pagamento.
///
/// <para>
/// É o elo que a decisão Q24 chamou de cobrança e baixa automática. A baixa
/// manual continua existindo e não muda — o escritório precisa poder registrar
/// um pagamento que entrou por fora, e precisa continuar funcionando nos dias
/// em que o PSP estiver fora do ar.
/// </para>
/// </summary>
public static class Cobrancas
{
    public static IEndpointRouteBuilder MapCobrancas(this IEndpointRouteBuilder rotas)
    {
        rotas.MapPost("/recebiveis/{id:guid}/cobrar", Cobrar)
            .WithTags("Recebíveis")
            .WithName("CobrarRecebivel")
            .WithSummary("Emite boleto e Pix para um recebível")
            .WithDescription("Cria a cobrança no PSP e guarda o link onde o cliente escolhe entre boleto e Pix. Recusa cobrar duas vezes o mesmo recebível: a segunda emissão não substituiria a primeira, e o cliente receberia dois boletos.")
            .Produces<RecebivelCobrado>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        /*
         * O webhook é anônimo porque quem chama é o PSP, que não tem sessão
         * nossa. Quem autentica é o token combinado, conferido abaixo.
         */
        rotas.MapPost("/integracoes/asaas/webhook", Notificar)
            .AllowAnonymous()
            .WithTags("Integrações")
            .WithName("WebhookDoAsaas")
            .WithSummary("Recebe os avisos de pagamento do PSP")
            .ExcludeFromDescription();

        return rotas;
    }

    /* ------------------------------------------------------------ emitir */

    private static async Task<IResult> Cobrar(
        Guid id,
        NexoDbContext banco,
        ClienteDoAsaas asaas,
        IOptions<OpcoesDoAsaas> configuracao,
        IContextoDeTenant contexto,
        CancellationToken cancelamento)
    {
        var opcoes = configuracao.Value;

        if (!opcoes.Configurado)
        {
            return Problema("cobranca", "Cobrança automática não configurada",
                "Este ambiente não tem chave do PSP.",
                "Continue dando baixa à mão, ou configure a chave para ligar a cobrança automática.");
        }

        if (contexto.TenantAtual is not { } tenant) return Results.Unauthorized();

        var recebivel = await banco.Recebiveis
            .Include(r => r.Pessoa)
            .FirstOrDefaultAsync(r => r.Id == id, cancelamento);

        if (recebivel is null) return Results.NotFound();

        /*
         * Já cobrado devolve o que existe, em vez de recusar.
         *
         * Dois cliques no mesmo botão é o caso comum, e o segundo não pode
         * virar uma segunda cobrança do mesmo valor. Devolver o link que já
         * existe é o que a pessoa queria das duas vezes.
         */
        if (!string.IsNullOrEmpty(recebivel.CobrancaId))
            return Results.Ok(new RecebivelCobrado(recebivel.Id, recebivel.CobrancaId, recebivel.CobrancaUrl));

        if (recebivel.Situacao != SituacaoRecebivel.Aberto)
        {
            return Problema("situacao", "Nada a cobrar",
                recebivel.Situacao == SituacaoRecebivel.Pago
                    ? "Este recebível já foi baixado."
                    : "Este recebível foi cancelado.",
                "Cobrança só se emite para o que está em aberto.");
        }

        var pessoa = recebivel.Pessoa!;

        if (string.IsNullOrWhiteSpace(pessoa.Documento))
        {
            /*
             * Conferido aqui, e não deixado para o PSP recusar, porque o
             * conserto é no cadastro daqui. A mensagem do PSP diria "cpfCnpj
             * inválido", que é verdade e não ajuda quem está olhando a lista
             * de recebíveis.
             */
            return Problema("documento", "Falta o CPF ou CNPJ do cliente",
                $"{pessoa.Nome} está sem documento no cadastro.",
                "Boleto e Pix levam o documento do pagador. Preencha no cadastro da pessoa e cobre de novo.");
        }

        try
        {
            if (string.IsNullOrEmpty(pessoa.ClienteNoAsaas))
            {
                pessoa.ClienteNoAsaas = await asaas.GarantirCliente(
                    pessoa.Nome, pessoa.Documento, pessoa.Email, pessoa.Id.ToString(), cancelamento);
            }

            var cobranca = await asaas.CriarCobranca(
                pessoa.ClienteNoAsaas,
                recebivel.Valor,
                recebivel.Vencimento,
                recebivel.Descricao,
                Referencia.Montar(tenant, recebivel.Id),
                cancelamento);

            recebivel.CobrancaId = cobranca.Id;
            recebivel.CobrancaUrl = cobranca.InvoiceUrl;
            recebivel.AtualizadoEm = DateTimeOffset.UtcNow;

            await banco.SaveChangesAsync(cancelamento);

            return Results.Ok(new RecebivelCobrado(recebivel.Id, cobranca.Id, cobranca.InvoiceUrl));
        }
        catch (FalhaNoAsaas falha)
        {
            return Problema("cobranca", "O PSP recusou a cobrança", falha.Message,
                "Confira o cadastro do cliente e tente de novo. Se persistir, a baixa manual continua disponível.");
        }
    }

    /* ---------------------------------------------------------- notificar */

    /// <summary>
    /// O aviso de que algo aconteceu com uma cobrança.
    ///
    /// <para>
    /// <b>Responde 200 quase sempre, e isso é deliberado.</b> O Asaas para a
    /// fila do webhook depois de 15 falhas seguidas, e eventos parados somem em
    /// 14 dias. Um evento que não entendemos, ou que aponta para um recebível
    /// que não existe mais, é permanente: repetir não conserta, e insistir
    /// derruba a fila inteira — inclusive os avisos de pagamento que
    /// importam. Erro só sai daqui quando repetir tem chance de funcionar, como
    /// banco fora do ar.
    /// </para>
    /// </summary>
    private static async Task<IResult> Notificar(
        HttpContext http,
        AvisoDoAsaas aviso,
        NexoDbContext banco,
        IOptions<OpcoesDoAsaas> configuracao,
        ILoggerFactory registros,
        CancellationToken cancelamento)
    {
        var opcoes = configuracao.Value;
        var registro = registros.CreateLogger("Cobranca");

        if (!Autentico(http, opcoes.TokenDoWebhook))
        {
            registro.LogWarning("Aviso do PSP recusado: token do webhook não confere.");
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(aviso.Id) || string.IsNullOrWhiteSpace(aviso.Event))
            return Results.Ok();

        if (aviso.Payment is not { } pagamento) return Results.Ok();

        if (!Referencia.Separar(pagamento.ExternalReference, out var tenant, out var recebivelId))
        {
            /*
             * Cobrança criada fora do Nexo — pelo painel do PSP, por exemplo.
             * Não é erro nosso e não tem o que baixar: reconhece e sai.
             */
            registro.LogInformation("Aviso {Evento} sem referência do Nexo, ignorado.", aviso.Event);
            return Results.Ok();
        }

        /*
         * A partir daqui a requisição tem tenant, e todo o resto volta a ser
         * protegido pela RLS como qualquer outra. Precisa vir antes do primeiro
         * toque no banco: a conexão grava `app.tenant_id` ao abrir.
         */
        ContextoDeTenantHttp.Fixar(http, tenant);

        banco.EventosDeCobranca.Add(new EventoDeCobranca
        {
            Id = aviso.Id,
            TenantId = tenant,
            Tipo = aviso.Event,
            RecebivelId = recebivelId,
            RecebidoEm = DateTimeOffset.UtcNow,
        });

        var recebivel = await banco.Recebiveis
            .FirstOrDefaultAsync(r => r.Id == recebivelId, cancelamento);

        if (recebivel is null)
        {
            registro.LogWarning("Aviso {Evento} para recebível {Id} que não existe.", aviso.Event, recebivelId);
            await GravarIgnorandoRepetido(banco, cancelamento);
            return Results.Ok();
        }

        Aplicar(aviso.Event, pagamento, recebivel);

        await GravarIgnorandoRepetido(banco, cancelamento);
        return Results.Ok();
    }

    /// <summary>
    /// O que cada aviso faz com o recebível.
    ///
    /// <para>
    /// <b>Confirmado e recebido dão a mesma baixa.</b> Os dois existem porque o
    /// dinheiro chega em dois tempos: o cliente paga, e depois o valor fica
    /// disponível na conta do PSP. Para o escritório, quem responde "este
    /// cliente está em dia" é o primeiro. Aceitar os dois e deixar o que chegar
    /// primeiro baixar é o que faz o Pix baixar na hora e o boleto baixar no
    /// dia em que foi pago, e não no dia seguinte.
    /// </para>
    /// <para>
    /// <b>Estorno desfaz a baixa</b>, e não cancela o recebível: o valor volta
    /// a ser devido. Cancelar apagaria a cobrança da conta do escritório, que é
    /// o contrário do que um estorno significa.
    /// </para>
    /// <para>
    /// Todo o resto é reconhecido e ignorado. Cobrança visualizada, boleto
    /// impresso, cobrança vencida — a tela já sabe calcular vencido sozinha,
    /// pela data.
    /// </para>
    /// </summary>
    private static void Aplicar(string evento, PagamentoDoAsaas pagamento, Recebivel recebivel)
    {
        switch (evento)
        {
            case "PAYMENT_CONFIRMED":
            case "PAYMENT_RECEIVED":
                if (recebivel.Situacao != SituacaoRecebivel.Aberto) return;

                recebivel.Situacao = SituacaoRecebivel.Pago;
                recebivel.ValorPago = pagamento.Value ?? recebivel.Valor;
                recebivel.PagoEm = Data(pagamento.ClientPaymentDate)
                    ?? Data(pagamento.PaymentDate)
                    ?? Data(pagamento.ConfirmedDate)
                    ?? DateOnly.FromDateTime(DateTime.UtcNow);
                recebivel.OrigemDaBaixa = OrigensDeBaixa.Cobranca;
                recebivel.AtualizadoEm = DateTimeOffset.UtcNow;
                return;

            case "PAYMENT_REFUNDED":
            case "PAYMENT_RECEIVED_IN_CASH_UNDONE":
                if (recebivel.Situacao != SituacaoRecebivel.Pago) return;

                recebivel.Situacao = SituacaoRecebivel.Aberto;
                recebivel.ValorPago = null;
                recebivel.PagoEm = null;
                recebivel.OrigemDaBaixa = string.Empty;
                recebivel.AtualizadoEm = DateTimeOffset.UtcNow;
                return;
        }
    }

    /// <summary>
    /// Grava, e trata a chave repetida como sucesso.
    ///
    /// A repetição é o caminho normal, não a exceção: a entrega do Asaas é
    /// <i>at least once</i>, então o mesmo aviso chega de novo. O banco recusar
    /// a segunda inserção é exatamente o desenho — e recusar dentro da mesma
    /// transação descarta junto a baixa que ela traria.
    /// </summary>
    private static async Task GravarIgnorandoRepetido(
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        try
        {
            await banco.SaveChangesAsync(cancelamento);
        }
        catch (DbUpdateException erro) when (erro.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        })
        {
            /* Já processado. Nada a fazer, e o 200 do chamador diz isso ao PSP. */
        }
    }

    /* ------------------------------------------------------------- apoio */

    /// <summary>
    /// Confere o token do webhook sem vazar por quanto tempo a comparação levou.
    ///
    /// Comparar string com <c>==</c> para em cima do primeiro caractere
    /// diferente, e a diferença de tempo entre "errou na primeira letra" e
    /// "errou na última" é medível por quem tenta adivinhar. É um segredo
    /// pequeno e estável, exatamente o tipo que compensa adivinhar.
    /// </summary>
    private static bool Autentico(HttpContext http, string esperado)
    {
        if (string.IsNullOrEmpty(esperado)) return false;

        var recebido = http.Request.Headers["asaas-access-token"].ToString();
        if (string.IsNullOrEmpty(recebido)) return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(recebido),
            Encoding.UTF8.GetBytes(esperado));
    }

    private static DateOnly? Data(string? valor) =>
        DateOnly.TryParse(valor, out var data) ? data : null;

    private static IResult Problema(string campo, string titulo, string descricao, string sugestao) =>
        Results.Json(
            new RespostaComProblemas([new Problema(campo, titulo, descricao, sugestao)]),
            statusCode: 422);
}

/// <summary>
/// O campo livre que o PSP devolve intacto, e que amarra a cobrança de volta.
///
/// <para>
/// <b>Leva o tenant junto, e não só o recebível.</b> O webhook chega sem
/// sessão, então não há claim de onde tirar quem é o dono daquela linha — e sem
/// tenant a RLS não devolve nada. Consultar sem isolamento para descobrir o
/// tenant abriria um caminho privilegiado no único lugar da aplicação que
/// atende requisição anônima, que é o pior lugar possível para abrir um.
/// </para>
/// <para>
/// O que se confia aqui é dado nosso voltando: fomos nós que escrevemos esta
/// string ao criar a cobrança. Quem prova que a notificação é do PSP é o token
/// do webhook; sem ele, nada nesta string é sequer lido.
/// </para>
/// </summary>
public static class Referencia
{
    public static string Montar(Guid tenant, Guid recebivel) => $"{tenant}/{recebivel}";

    public static bool Separar(string? referencia, out Guid tenant, out Guid recebivel)
    {
        tenant = Guid.Empty;
        recebivel = Guid.Empty;

        var partes = referencia?.Split('/');
        if (partes is not { Length: 2 }) return false;

        return Guid.TryParse(partes[0], out tenant) && Guid.TryParse(partes[1], out recebivel);
    }
}

public record RecebivelCobrado(Guid Id, string CobrancaId, string CobrancaUrl);

/// <param name="Id">O identificador da entrega, e a chave da idempotência.</param>
public record AvisoDoAsaas(
    string? Id,
    string? Event,
    [property: JsonPropertyName("payment")] PagamentoDoAsaas? Payment);

/// <param name="ClientPaymentDate">Quando o cliente pagou, que é a data que interessa ao escritório.</param>
public record PagamentoDoAsaas(
    string? Id,
    string? ExternalReference,
    decimal? Value,
    string? Status,
    string? ConfirmedDate,
    string? PaymentDate,
    string? ClientPaymentDate);
