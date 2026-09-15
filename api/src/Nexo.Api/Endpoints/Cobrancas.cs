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
        rotas.MapPost("/lancamentos/{id:guid}/cobrar", Cobrar)
            .WithTags("Lançamentos")
            .WithName("CobrarLancamento")
            .WithSummary("Emite boleto e Pix para um lançamento")
            .WithDescription("Cria a cobrança no PSP e guarda o link onde o cliente escolhe entre boleto e Pix. Recusa cobrar duas vezes o mesmo lançamento: a segunda emissão não substituiria a primeira, e o cliente receberia dois boletos.")
            .Produces<LancamentoCobrado>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        rotas.MapGet("/cobrancas/divergencias", ListarDivergencias)
            .WithTags("Lançamentos")
            .WithName("ListarDivergenciasDeCobranca")
            .WithSummary("Pagamentos do PSP que não puderam ser aplicados")
            .WithDescription("Os avisos de pagamento que chegaram para lançamentos que já não estavam em aberto. Decidir entre devolver o valor e reabrir o título é trabalho de gente.")
            .Produces<List<DivergenciaDeCobranca>>();

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

        var lancamento = await banco.Lancamentos
            .Include(r => r.Pessoa)
            .FirstOrDefaultAsync(r => r.Id == id, cancelamento);

        if (lancamento is null) return Results.NotFound();

        /* Cobrar é pedir dinheiro a um cliente. No lançamento a pagar, quem paga é o escritório. */
        if (lancamento.Natureza != NaturezaLancamento.Receber)
        {
            return Problema("natureza", "Nada a cobrar",
                "Este lançamento é a pagar: quem paga é o escritório.",
                "Cobrança só se emite para o que é a receber.");
        }

        /*
         * Já cobrado devolve o que existe, em vez de recusar.
         *
         * Dois cliques no mesmo botão é o caso comum, e o segundo não pode
         * virar uma segunda cobrança do mesmo valor. Devolver o link que já
         * existe é o que a pessoa queria das duas vezes.
         */
        if (!string.IsNullOrEmpty(lancamento.CobrancaId))
            return Results.Ok(new LancamentoCobrado(lancamento.Id, lancamento.CobrancaId, lancamento.CobrancaUrl));

        if (lancamento.Situacao != SituacaoLancamento.Aberto)
        {
            return Problema("situacao", "Nada a cobrar",
                lancamento.Situacao switch
                {
                    SituacaoLancamento.Pago => "Este lançamento já foi baixado.",
                    SituacaoLancamento.Renegociado => "Este lançamento foi renegociado: cobre as parcelas novas.",
                    _ => "Este lançamento foi cancelado.",
                },
                "Cobrança só se emite para o que está em aberto.");
        }

        /*
         * Sem conta marcada para receber, o aviso de pagamento não teria onde
         * baixar e viraria divergência. Recusar aqui evita emitir um boleto que
         * já nasce sem destino.
         */
        if (!await banco.ContasBancarias.AnyAsync(conta => conta.RecebeCobrancas && conta.Ativa, cancelamento))
        {
            return Problema("contaId", "Nenhuma conta recebe as cobranças",
                "Não há conta marcada para receber o que o PSP cobrar.",
                "Em Contas bancárias, marque a conta do PSP como a que recebe as cobranças.");
        }

        var pessoa = lancamento.Pessoa!;

        if (string.IsNullOrWhiteSpace(pessoa.Documento))
        {
            /*
             * Conferido aqui, e não deixado para o PSP recusar, porque o
             * conserto é no cadastro daqui. A mensagem do PSP diria "cpfCnpj
             * inválido", que é verdade e não ajuda quem está olhando a lista
             * de lançamentos.
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
                lancamento.Valor,
                lancamento.Vencimento,
                lancamento.Descricao,
                Referencia.Montar(tenant, lancamento.Id),
                cancelamento);

            lancamento.CobrancaId = cobranca.Id;
            lancamento.CobrancaUrl = cobranca.InvoiceUrl;
            lancamento.AtualizadoEm = DateTimeOffset.UtcNow;

            await banco.SaveChangesAsync(cancelamento);

            return Results.Ok(new LancamentoCobrado(lancamento.Id, cobranca.Id, cobranca.InvoiceUrl));
        }
        catch (FalhaNoAsaas falha)
        {
            return Problema("cobranca", "O PSP recusou a cobrança", falha.Message,
                "Confira o cadastro do cliente e tente de novo. Se persistir, a baixa manual continua disponível.");
        }
    }

    /* ----------------------------------------------------------- retirar */

    /// <summary>
    /// Tira do PSP a cobrança de um lançamento que vai deixar de estar em aberto
    /// por outro caminho que não o pagamento: cancelamento ou baixa à mão.
    ///
    /// <para>
    /// Devolve a falha quando o PSP não retira, e aí quem chamou precisa parar
    /// sem gravar nada. A recusa mais provável é a cobrança já ter sido
    /// paga: o cliente pagou há instantes e o aviso ainda não chegou. Cancelar
    /// por cima faria esse aviso chegar para um lançamento cancelado.
    /// </para>
    /// <para>
    /// <b>Sem integração configurada, segue sem chamar ninguém</b>, e guarda a
    /// referência. O escritório desligou a cobrança automática, e travar o
    /// cancelamento de tudo o que foi cobrado antes seria punir quem desligou. A
    /// referência fica porque é por ela que alguém acha a cobrança no painel do
    /// PSP para tirá-la de lá à mão.
    /// </para>
    /// </summary>
    internal static async Task<FalhaAoRetirar?> TentarRetirarDoPsp(
        Lancamento lancamento,
        ClienteDoAsaas asaas,
        OpcoesDoAsaas opcoes,
        ILogger registro,
        CancellationToken cancelamento)
    {
        if (string.IsNullOrEmpty(lancamento.CobrancaId)) return null;

        if (!opcoes.Configurado)
        {
            registro.LogWarning(
                "Lançamento {Id} tem a cobrança {Cobranca} no PSP e a integração está desligada: a cobrança continua pagável lá.",
                lancamento.Id, lancamento.CobrancaId);
            return null;
        }

        try
        {
            await asaas.ExcluirCobranca(lancamento.CobrancaId, cancelamento);
        }
        catch (FalhaNoAsaas falha)
        {
            return new FalhaAoRetirar("O PSP não retirou a cobrança", falha.Message,
                "Se o cliente acabou de pagar, a baixa chega sozinha em instantes. Senão, tente de novo.");
        }
        catch (Exception erro) when (erro is HttpRequestException or TaskCanceledException)
        {
            return new FalhaAoRetirar("Não foi possível falar com o PSP",
                "A cobrança continua pagável lá, e por isso nada foi alterado aqui.",
                "Tente de novo em alguns minutos.");
        }

        /* O link morreu com a cobrança. Deixá-lo aqui faria um estorno futuro
           reabrir o lançamento apontando para um boleto que não existe mais. */
        lancamento.CobrancaId = string.Empty;
        lancamento.CobrancaUrl = string.Empty;
        return null;
    }

    /// <summary>
    /// A mesma retirada, com a falha já pronta para devolver a quem chamou.
    ///
    /// A baixa em lote usa a versão de cima porque precisa do motivo por item,
    /// e não de uma resposta para a requisição inteira.
    /// </summary>
    internal static async Task<IResult?> RetirarDoPsp(
        Lancamento lancamento,
        ClienteDoAsaas asaas,
        OpcoesDoAsaas opcoes,
        ILogger registro,
        CancellationToken cancelamento) =>
        await TentarRetirarDoPsp(lancamento, asaas, opcoes, registro, cancelamento) is { } falha
            ? Problema("cobranca", falha.Titulo, falha.Descricao, falha.Sugestao)
            : null;

    internal sealed record FalhaAoRetirar(string Titulo, string Descricao, string Sugestao);

    /* ------------------------------------------------------ divergências */

    /// <summary>
    /// Os pagamentos marcados como divergência, dos mais novos aos mais antigos.
    ///
    /// Sem filtro de tenant na consulta, como em todo o resto: quem limita é a
    /// política de linha, alimentada pela sessão.
    /// </summary>
    private static async Task<IResult> ListarDivergencias(NexoDbContext banco, CancellationToken cancelamento)
    {
        var eventos = await banco.EventosDeCobranca.AsNoTracking()
            .Where(evento => evento.Divergencia != "")
            .OrderByDescending(evento => evento.RecebidoEm)
            .Take(50)
            .ToListAsync(cancelamento);

        var ids = eventos
            .Where(evento => evento.LancamentoId != null)
            .Select(evento => evento.LancamentoId!.Value)
            .Distinct()
            .ToList();

        var lancamentos = await banco.Lancamentos.AsNoTracking()
            .Where(lancamento => ids.Contains(lancamento.Id))
            .Select(lancamento => new { lancamento.Id, lancamento.Descricao, lancamento.Valor, Nome = lancamento.Pessoa!.Nome })
            .ToDictionaryAsync(lancamento => lancamento.Id, cancelamento);

        return Results.Ok(eventos
            .Select(evento =>
            {
                lancamentos.TryGetValue(evento.LancamentoId ?? Guid.Empty, out var lancamento);
                return new DivergenciaDeCobranca(
                    evento.Id, evento.LancamentoId, evento.Tipo, evento.Divergencia, evento.RecebidoEm,
                    lancamento?.Nome, lancamento?.Descricao, lancamento?.Valor);
            })
            .ToList());
    }

    /* ---------------------------------------------------------- notificar */

    /// <summary>
    /// O aviso de que algo aconteceu com uma cobrança.
    ///
    /// <para>
    /// <b>Responde 200 quase sempre, e isso é deliberado.</b> O Asaas para a
    /// fila do webhook depois de 15 falhas seguidas, e eventos parados somem em
    /// 14 dias. Um evento que não entendemos, ou que aponta para um lançamento
    /// que não existe mais, é permanente: repetir não conserta, e insistir
    /// derruba a fila inteira — inclusive os avisos de pagamento que
    /// importam. Erro só sai daqui quando repetir tem chance de funcionar, como
    /// banco fora do ar ou outra operação mudando o mesmo lançamento no mesmo
    /// instante.
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

        if (!Referencia.Separar(pagamento.ExternalReference, out var tenant, out var lancamentoId))
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

        var evento = new EventoDeCobranca
        {
            Id = aviso.Id,
            TenantId = tenant,
            Tipo = aviso.Event,
            LancamentoId = lancamentoId,
            RecebidoEm = DateTimeOffset.UtcNow,
        };

        banco.EventosDeCobranca.Add(evento);

        var lancamento = await banco.Lancamentos
            .FirstOrDefaultAsync(r => r.Id == lancamentoId, cancelamento);

        if (lancamento is null)
        {
            registro.LogWarning("Aviso {Evento} para lançamento {Id} que não existe.", aviso.Event, lancamentoId);
            await GravarIgnorandoRepetido(banco, cancelamento);
            return Results.Ok();
        }

        var divergencia = await Aplicar(aviso.Event, pagamento, lancamento, banco, cancelamento);

        if (divergencia.Length > 0)
        {
            /*
             * O aviso não mudou nada, e isso não pode passar calado: é dinheiro
             * que entrou para um lançamento que já não esperava por ele. O evento
             * fica marcado para alguém conferir. Devolver ao cliente ou reabrir
             * o título é decisão de gente, e não do webhook.
             */
            evento.Divergencia = divergencia;
            registro.LogWarning(
                "Aviso {Evento} não aplicado ao lançamento {Id}: {Divergencia}",
                aviso.Event, lancamentoId, divergencia);
        }

        await GravarIgnorandoRepetido(banco, cancelamento);
        return Results.Ok();
    }

    /// <summary>
    /// O que cada aviso faz com o lançamento. Devolve a divergência, quando o
    /// aviso trazia algo que não pôde ser aplicado.
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
    /// <b>Estorno desfaz a baixa</b>, e não cancela o lançamento: o valor volta
    /// a ser devido. Cancelar apagaria a cobrança da conta do escritório, que é
    /// o contrário do que um estorno significa.
    /// </para>
    /// <para>
    /// Todo o resto é reconhecido e ignorado. Cobrança visualizada, boleto
    /// impresso, cobrança vencida — a tela já sabe calcular vencido sozinha,
    /// pela data.
    /// </para>
    /// </summary>
    private static async Task<string> Aplicar(
        string evento,
        PagamentoDoAsaas pagamento,
        Lancamento lancamento,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        switch (evento)
        {
            case "PAYMENT_CONFIRMED":
            case "PAYMENT_RECEIVED":
                if (lancamento.Situacao != SituacaoLancamento.Aberto)
                {
                    /*
                     * Confirmado e depois recebido é o mesmo pagamento em dois
                     * tempos, e não divergência. Qualquer outro caso é dinheiro
                     * chegando para quem não esperava: baixado à mão e pago de
                     * novo pelo PSP, ou cancelado e pago mesmo assim.
                     */
                    if (lancamento.Situacao == SituacaoLancamento.Pago
                        && lancamento.OrigemDaBaixa == OrigensDeBaixa.Cobranca)
                        return string.Empty;

                    return lancamento.Situacao == SituacaoLancamento.Pago
                        ? "Pagamento pelo PSP para um lançamento já baixado à mão."
                        : $"Pagamento pelo PSP para um lançamento {lancamento.Situacao.ToString().ToLowerInvariant()}.";
                }

                var pagoEm = Data(pagamento.ClientPaymentDate)
                    ?? Data(pagamento.PaymentDate)
                    ?? Data(pagamento.ConfirmedDate)
                    ?? DateOnly.FromDateTime(DateTime.UtcNow);

                /*
                 * O dinheiro entra na conta marcada para receber as cobranças.
                 * Sem ela, ou com ela começando depois do pagamento, o movimento
                 * não tem onde caber: a baixa fica para alguém dar à mão,
                 * escolhendo a conta, e o aviso fica marcado para isso não
                 * passar calado.
                 */
                var contaDoPsp = await banco.ContasBancarias.AsNoTracking()
                    .FirstOrDefaultAsync(conta => conta.RecebeCobrancas && conta.Ativa, cancelamento);

                if (contaDoPsp is null)
                    return "Pagamento pelo PSP sem conta marcada para receber as cobranças: marque a conta e dê a baixa à mão.";

                if (pagoEm < contaDoPsp.SaldoInicialEm)
                    return $"Pagamento pelo PSP de {pagoEm:dd/MM/yyyy}, antes do saldo inicial da conta {contaDoPsp.Nome}: confira e dê a baixa à mão.";

                lancamento.Situacao = SituacaoLancamento.Pago;
                lancamento.ValorPago = pagamento.Value ?? lancamento.Valor;
                lancamento.PagoEm = pagoEm;
                lancamento.OrigemDaBaixa = OrigensDeBaixa.Cobranca;
                lancamento.AtualizadoEm = DateTimeOffset.UtcNow;

                banco.MovimentosDeConta.Add(
                    MovimentoDeConta.DaBaixa(lancamento, contaDoPsp.Id, lancamento.ValorPago.Value, pagoEm));
                return string.Empty;

            case "PAYMENT_REFUNDED":
            case "PAYMENT_RECEIVED_IN_CASH_UNDONE":
                if (lancamento.Situacao != SituacaoLancamento.Pago) return string.Empty;

                var baixa = await banco.MovimentosDeConta.FirstOrDefaultAsync(
                    m => m.LancamentoId == lancamento.Id && m.Origem == OrigensDeMovimento.Baixa, cancelamento);

                if (baixa is not null)
                {
                    /*
                     * O dinheiro entrou de fato e voltou para o cliente, e o
                     * extrato do PSP mostra as duas linhas. A entrada fica, marcada
                     * como estornada, e a devolução sai ao lado dela: apagar a
                     * entrada deixaria o extrato daqui com uma linha a menos que o
                     * de lá. Baixa de antes das contas não tem movimento a compensar.
                     */
                    baixa.Origem = OrigensDeMovimento.BaixaEstornada;

                    banco.MovimentosDeConta.Add(new MovimentoDeConta
                    {
                        Id = Guid.NewGuid(),
                        TenantId = baixa.TenantId,
                        ContaId = baixa.ContaId,
                        LancamentoId = lancamento.Id,
                        Data = DateOnly.FromDateTime(DateTime.Today),
                        Valor = -baixa.Valor,
                        Descricao = baixa.Descricao,
                        Origem = OrigensDeMovimento.EstornoDoPsp,
                        CriadoEm = DateTimeOffset.UtcNow,
                    });
                }

                lancamento.Situacao = SituacaoLancamento.Aberto;
                lancamento.ValorPago = null;
                lancamento.PagoEm = null;
                lancamento.OrigemDaBaixa = string.Empty;
                lancamento.AtualizadoEm = DateTimeOffset.UtcNow;
                return string.Empty;
        }

        return string.Empty;
    }

    /// <summary>
    /// Grava, e trata a chave repetida como sucesso.
    ///
    /// A repetição é o caminho normal, não a exceção: a entrega do Asaas é
    /// <i>at least once</i>, então o mesmo aviso chega de novo. O banco recusar
    /// a segunda inserção é exatamente o desenho — e recusar dentro da mesma
    /// transação descarta junto a baixa que ela traria.
    ///
    /// Conflito de concorrência <b>não</b> é engolido: sobe como erro, a
    /// transação inteira volta, inclusive o registro do evento, e o PSP reenvia.
    /// Na segunda tentativa o lançamento já está no estado novo.
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
/// <b>Leva o tenant junto, e não só o lançamento.</b> O webhook chega sem
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
    public static string Montar(Guid tenant, Guid lancamento) => $"{tenant}/{lancamento}";

    public static bool Separar(string? referencia, out Guid tenant, out Guid lancamento)
    {
        tenant = Guid.Empty;
        lancamento = Guid.Empty;

        var partes = referencia?.Split('/');
        if (partes is not { Length: 2 }) return false;

        return Guid.TryParse(partes[0], out tenant) && Guid.TryParse(partes[1], out lancamento);
    }
}

public record LancamentoCobrado(Guid Id, string CobrancaId, string CobrancaUrl);

/// <param name="EventoId">O identificador do aviso no PSP, para achá-lo no painel de lá.</param>
/// <param name="NomeDaPessoa">Nulo quando o lançamento não existe mais.</param>
public record DivergenciaDeCobranca(
    string EventoId,
    Guid? LancamentoId,
    string Tipo,
    string Divergencia,
    DateTimeOffset RecebidoEm,
    string? NomeDaPessoa,
    string? Descricao,
    decimal? Valor);

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
