using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexo.Api.Cobranca;
using Nexo.Api.Dados;
using Nexo.Api.Dominio;

namespace Nexo.Api.Endpoints;

/// <summary>
/// O que o escritório tem a receber, e a baixa quando o dinheiro entra.
/// </summary>
public static class Lancamentos
{
    public static IEndpointRouteBuilder MapLancamentos(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/lancamentos").WithTags("Lançamentos");

        grupo.MapGet("/", Listar)
            .WithName("ListarLancamentos")
            .WithSummary("Lista os lançamentos")
            .Produces<PaginaDeLancamentos>();

        grupo.MapPost("/", Criar)
            .WithName("CriarLancamentoAvulso")
            .WithSummary("Lança uma cobrança fora de contrato")
            .WithDescription("Para o que o escritório faz e não é mensalidade: declaração de imposto de renda, abertura de empresa, certidão. Fica sem contrato por trás.")
            .Produces<LancamentoNaLista>(StatusCodes.Status201Created)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPost("/{id:guid}/baixar", Baixar)
            .WithName("BaixarLancamento")
            .WithSummary("Registra o recebimento")
            .Produces<LancamentoNaLista>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapPost("/{id:guid}/cancelar", Cancelar)
            .WithName("CancelarLancamento")
            .WithSummary("Cancela um lançamento em aberto")
            .WithDescription("Libera a competência para ser gerada de novo, sem apagar o histórico do que foi cancelado.")
            .Produces<LancamentoNaLista>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapPost("/{id:guid}/estornar", Estornar)
            .WithName("EstornarLancamento")
            .WithSummary("Desfaz a baixa")
            .WithDescription("Existe porque baixa errada acontece, e sem estorno a correção viraria um segundo registro inventado.")
            .Produces<LancamentoNaLista>()
            .Produces(StatusCodes.Status404NotFound);

        return rotas;
    }

    /// <summary>
    /// Lista os lançamentos, paginados, com os totais do período.
    ///
    /// <para>
    /// <b>Os totais são somados no banco, sobre o conjunto inteiro — nunca
    /// sobre a página.</b> Somar a página daria um número errado com cara de
    /// certo: "em aberto" mostraria só o que coube na tela, e ninguém
    /// desconfiaria.
    /// </para>
    /// <para>
    /// E os totais seguem a <b>competência</b>, não a situação. Filtrar por
    /// "Pagos" e ver "em aberto: R$ 0,00" seria honesto e inútil; o escritório
    /// quer saber quanto o mês tem em aberto enquanto olha o que já entrou.
    /// A lista mostra a fatia escolhida, os totais mostram o mês.
    /// </para>
    /// </summary>
    private static async Task<IResult> Listar(
        NexoDbContext banco,
        CancellationToken cancelamento,
        [FromQuery] SituacaoLancamento? situacao = null,
        [FromQuery] int? ano = null,
        [FromQuery] int? mes = null,
        [FromQuery] string? busca = null,
        [FromQuery] DateOnly? vencimentoDe = null,
        [FromQuery] DateOnly? vencimentoAte = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 25,
        [FromQuery] OrdemDeLancamentos ordenarPor = OrdemDeLancamentos.Vencimento,
        [FromQuery] Direcao direcao = Direcao.Crescente)
    {
        pagina = Math.Max(1, pagina);
        tamanho = Math.Clamp(tamanho, 1, 200);

        /* O período: o que os totais enxergam. */
        var doPeriodo = banco.Lancamentos.AsNoTracking();
        if (ano is { } a) doPeriodo = doPeriodo.Where(r => r.CompetenciaAno == a);
        if (mes is { } m) doPeriodo = doPeriodo.Where(r => r.CompetenciaMes == m);

        /* A fatia: o que a lista mostra. */
        var daLista = situacao is { } filtro ? doPeriodo.Where(r => r.Situacao == filtro) : doPeriodo;

        /*
         * Busca e período de vencimento recortam a lista, e não os totais. É a
         * mesma regra da situação: os totais respondem quanto o mês tem em
         * aberto, e essa pergunta não muda porque alguém procurou um cliente.
         *
         * O código do cliente é comparado inteiro, e não por pedaço: procurar
         * pelo cliente 1 não pode trazer o 10, o 11 e o 12 junto.
         */
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim();
            daLista = daLista.Where(r =>
                EF.Functions.ILike(r.Descricao, $"%{termo}%")
                || EF.Functions.ILike(r.Pessoa!.Nome, $"%{termo}%")
                || r.Pessoa!.Codigo == termo);
        }

        if (vencimentoDe is { } de) daLista = daLista.Where(r => r.Vencimento >= de);
        if (vencimentoAte is { } ate) daLista = daLista.Where(r => r.Vencimento <= ate);

        var total = await daLista.CountAsync(cancelamento);

        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var emAberto = doPeriodo.Where(r => r.Situacao == SituacaoLancamento.Aberto);

        /*
         * O molde `(decimal?)` não é enfeite: SUM de conjunto vazio devolve
         * NULL no SQL, e sem o tipo anulável o EF tenta encaixar isso num
         * decimal e estoura. Mês sem nenhum lançamento é o caso mais comum de
         * todos — é o mês que ainda não começou.
         */
        var totalEmAberto = await emAberto
            .Select(r => (decimal?)r.Valor).SumAsync(cancelamento) ?? 0m;

        var totalVencido = await emAberto.Where(r => r.Vencimento < hoje)
            .Select(r => (decimal?)r.Valor).SumAsync(cancelamento) ?? 0m;

        var totalRecebido = await doPeriodo.Where(r => r.Situacao == SituacaoLancamento.Pago)
            .Select(r => r.ValorPago).SumAsync(cancelamento) ?? 0m;

        var itens = await Ordenar(daLista, ordenarPor, direcao)
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .Select(lancamento => new LancamentoNaLista(
                lancamento.Id,
                lancamento.Pessoa!.Codigo,
                lancamento.Pessoa!.Nome,
                lancamento.Descricao,
                lancamento.CompetenciaAno,
                lancamento.CompetenciaMes,
                lancamento.Valor,
                lancamento.Vencimento,
                lancamento.Situacao,
                lancamento.ValorPago,
                lancamento.PagoEm,
                lancamento.OrigemDaBaixa,
                lancamento.MotivoDoCancelamento,
                lancamento.ParcelaNumero,
                lancamento.ParcelasTotal,
                lancamento.RenegociadoDeId,
                lancamento.CobrancaUrl))
            .ToListAsync(cancelamento);

        return Results.Ok(new PaginaDeLancamentos(
            itens, total, pagina, tamanho, totalEmAberto, totalVencido, totalRecebido));
    }

    /// <summary>
    /// A ordem da listagem, decidida no banco — ver <see cref="Direcao"/>.
    ///
    /// <para>
    /// <b>Por vencimento é o padrão</b>, porque a pergunta que traz alguém a
    /// esta tela é o que vence primeiro. Dentro do mesmo dia vem o código do
    /// cliente, comparado pelo comprimento antes do texto: ele é número puro
    /// guardado como texto, e sem isso o 10 viria antes do 2.
    /// </para>
    /// <para>
    /// <b>Competência ordena por ano e depois por mês</b>, e não pelo texto
    /// "03/2026" que a tela mostra: em texto, março de qualquer ano viria antes
    /// de dezembro de qualquer outro.
    /// </para>
    /// <para>
    /// Toda ordem daqui termina no identificador. Em valor, o empate é a regra:
    /// mensalidade gerada de contrato sai idêntica para a carteira inteira.
    /// </para>
    /// </summary>
    private static IOrderedQueryable<Lancamento> Ordenar(
        IQueryable<Lancamento> consulta,
        OrdemDeLancamentos por,
        Direcao direcao)
    {
        var decrescente = direcao == Direcao.Decrescente;

        IOrderedQueryable<Lancamento> ordenada = por switch
        {
            OrdemDeLancamentos.Cliente => decrescente
                ? consulta.OrderByDescending(lancamento => lancamento.Pessoa!.Nome)
                : consulta.OrderBy(lancamento => lancamento.Pessoa!.Nome),

            OrdemDeLancamentos.Valor => decrescente
                ? consulta.OrderByDescending(lancamento => lancamento.Valor)
                : consulta.OrderBy(lancamento => lancamento.Valor),

            OrdemDeLancamentos.Competencia => decrescente
                ? consulta.OrderByDescending(lancamento => lancamento.CompetenciaAno)
                    .ThenByDescending(lancamento => lancamento.CompetenciaMes)
                : consulta.OrderBy(lancamento => lancamento.CompetenciaAno)
                    .ThenBy(lancamento => lancamento.CompetenciaMes),

            _ => decrescente
                ? consulta.OrderByDescending(lancamento => lancamento.Vencimento)
                    .ThenByDescending(lancamento => lancamento.Pessoa!.Codigo.Length)
                    .ThenByDescending(lancamento => lancamento.Pessoa!.Codigo)
                : consulta.OrderBy(lancamento => lancamento.Vencimento)
                    .ThenBy(lancamento => lancamento.Pessoa!.Codigo.Length)
                    .ThenBy(lancamento => lancamento.Pessoa!.Codigo),
        };

        return ordenada.ThenBy(lancamento => lancamento.Id);
    }

    /// <summary>
    /// Cria um lançamento sem contrato por trás.
    ///
    /// <para>
    /// <b>Vários avulsos podem dividir a mesma competência</b>, e isso não é
    /// descuido: o índice único cobre <c>ContratoId + competência</c>, e no
    /// Postgres duas linhas com <c>NULL</c> nessa coluna nunca colidem. A
    /// proteção existe contra cobrar a mesma mensalidade duas vezes, que é
    /// erro de máquina; o escritório que emite três certidões no mesmo mês
    /// está fazendo o trabalho dele.
    /// </para>
    /// <para>
    /// A competência é pedida, e não deduzida do vencimento, porque os dois
    /// quase nunca coincidem — serviço prestado em janeiro costuma vencer em
    /// fevereiro, e é por competência que o escritório fecha o mês.
    /// </para>
    /// </summary>
    private static async Task<IResult> Criar(
        [FromBody] DadosDoAvulso dados,
        NexoDbContext banco,
        IContextoDeTenant contexto,
        CancellationToken cancelamento)
    {
        if (contexto.TenantAtual is not { } tenant) return Results.Unauthorized();

        var problemas = await Conferir(dados, banco, cancelamento);
        if (problemas.Count > 0)
            return Results.Json(new RespostaComProblemas(problemas), statusCode: 422);

        var agora = DateTimeOffset.UtcNow;

        var lancamento = new Lancamento
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            PessoaId = dados.PessoaId,
            ContratoId = null,
            CompetenciaAno = dados.CompetenciaAno,
            CompetenciaMes = dados.CompetenciaMes,
            Descricao = dados.Descricao.Trim(),
            Valor = dados.Valor,
            Vencimento = dados.Vencimento,
            Situacao = SituacaoLancamento.Aberto,
            CriadoEm = agora,
            AtualizadoEm = agora,
        };

        banco.Lancamentos.Add(lancamento);
        await banco.SaveChangesAsync(cancelamento);

        await banco.Entry(lancamento).Reference(r => r.Pessoa).LoadAsync(cancelamento);

        return Results.Created($"/lancamentos/{lancamento.Id}", Detalhar(lancamento));
    }

    internal static async Task<List<Problema>> Conferir(
        DadosDoAvulso dados,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var problemas = new List<Problema>();

        /*
         * Não basta a pessoa existir: ela precisa carregar o papel de cliente.
         * Sem essa conferência, um fornecedor ou um colaborador entraria num
         * contrato por engano de escolha na lista, e o erro só apareceria na
         * hora de cobrar.
         */
        var pessoa = await banco.Pessoas.AsNoTracking()
            .Include(p => p.Papeis)
            .FirstOrDefaultAsync(p => p.Id == dados.PessoaId, cancelamento);

        if (pessoa is null)
        {
            problemas.Add(new Problema("pessoaId", "Cobrança sem cliente",
                "A pessoa informada não existe neste cadastro.",
                "Escolha alguém da lista."));
        }
        else if (pessoa.Papeis.All(p => p.Papel != Papel.Cliente))
        {
            problemas.Add(new Problema("pessoaId", "Cobrança sem cliente",
                $"“{pessoa.Nome}” não está marcada como cliente.",
                "Abra o cadastro dela e marque o papel Cliente."));
        }

        if (string.IsNullOrWhiteSpace(dados.Descricao))
        {
            problemas.Add(new Problema("descricao", "Falha na validação da cobrança",
                "A descrição não foi informada.",
                "Diga o que está sendo cobrado: “Declaração de IRPF 2026”, “Abertura de empresa”."));
        }

        if (dados.Valor <= 0)
        {
            problemas.Add(new Problema("valor", "Falha na validação da cobrança",
                "O valor precisa ser maior que zero.",
                "Informe quanto o cliente tem a pagar por este serviço."));
        }

        if (dados.CompetenciaMes is < 1 or > 12)
        {
            problemas.Add(new Problema("competenciaMes", "Competência inválida",
                $"O mês informado foi {dados.CompetenciaMes}.",
                "Informe um mês entre 1 e 12."));
        }

        /*
         * O intervalo é largo de propósito. O escritório lança competência
         * atrasada o tempo todo, e às vezes adiantada; o que isto barra é o
         * ano digitado errado por escorregão de tecla, que passaria despercebido
         * e sumiria do fechamento do mês.
         */
        if (dados.CompetenciaAno is < 2000 or > 2100)
        {
            problemas.Add(new Problema("competenciaAno", "Competência inválida",
                $"O ano informado foi {dados.CompetenciaAno}.",
                "Informe um ano entre 2000 e 2100."));
        }

        return problemas;
    }

    private static async Task<IResult> Baixar(
        Guid id,
        [FromBody] DadosDaBaixa dados,
        NexoDbContext banco,
        ClienteDoAsaas asaas,
        IOptions<OpcoesDoAsaas> configuracao,
        ILoggerFactory registros,
        CancellationToken cancelamento)
    {
        var lancamento = await banco.Lancamentos
            .Include(r => r.Pessoa)
            .FirstOrDefaultAsync(r => r.Id == id, cancelamento);

        if (lancamento is null) return Results.NotFound();

        if (lancamento.Situacao == SituacaoLancamento.Pago)
        {
            /*
             * Recusar em vez de sobrescrever. Baixar duas vezes costuma ser
             * clique repetido, e sobrescrever apagaria a data e o valor da
             * primeira baixa — que é exatamente o que alguém vai procurar
             * quando a conciliação não fechar.
             */
            return Problema("id", "Lançamento já baixado",
                $"Este lançamento foi baixado em {lancamento.PagoEm:dd/MM/yyyy}.",
                "Para corrigir o valor ou a data, estorne a baixa e registre de novo.");
        }

        if (lancamento.Situacao == SituacaoLancamento.Cancelado)
        {
            return Problema("id", "Lançamento cancelado",
                "Um lançamento cancelado não pode ser baixado.",
                "Se a cobrança voltou a valer, gere-a novamente.");
        }

        if (dados.ValorPago <= 0)
        {
            return Problema("valorPago", "Valor inválido",
                "O valor recebido precisa ser maior que zero.",
                "Informe quanto entrou de fato — pode ser diferente do valor cobrado.");
        }

        /*
         * Baixar à mão diz que o dinheiro entrou por fora. A cobrança do PSP sai
         * do ar antes, senão vira um segundo pagamento esperando acontecer. Se o
         * PSP não retira, nada é gravado aqui.
         */
        var naoRetirou = await Cobrancas.RetirarDoPsp(
            lancamento, asaas, configuracao.Value, registros.CreateLogger("Cobranca"), cancelamento);
        if (naoRetirou is not null) return naoRetirou;

        lancamento.Situacao = SituacaoLancamento.Pago;
        lancamento.ValorPago = dados.ValorPago;
        lancamento.PagoEm = dados.PagoEm ?? DateOnly.FromDateTime(DateTime.Today);
        lancamento.OrigemDaBaixa = OrigensDeBaixa.Manual;
        lancamento.AtualizadoEm = DateTimeOffset.UtcNow;

        if (await Gravar(banco, cancelamento) is { } conflito) return conflito;

        return Results.Ok(Detalhar(lancamento));
    }

    /// <summary>
    /// Cancela um lançamento em aberto.
    ///
    /// Cancelar libera a competência: o índice único ignora cancelados, então
    /// a mensalidade pode ser gerada de novo com o valor certo. É o caminho
    /// para consertar o erro comum — valor do contrato errado, mensalidades
    /// geradas, contrato corrigido — que antes não tinha conserto nenhum.
    /// </summary>
    private static async Task<IResult> Cancelar(
        Guid id,
        [FromBody] DadosDoCancelamento dados,
        NexoDbContext banco,
        ClienteDoAsaas asaas,
        IOptions<OpcoesDoAsaas> configuracao,
        ILoggerFactory registros,
        CancellationToken cancelamento)
    {
        var lancamento = await banco.Lancamentos
            .Include(r => r.Pessoa)
            .FirstOrDefaultAsync(r => r.Id == id, cancelamento);

        if (lancamento is null) return Results.NotFound();

        if (lancamento.Situacao == SituacaoLancamento.Pago)
        {
            /*
             * Cancelar o que já foi pago apagaria a entrada de dinheiro da
             * conta sem devolver nada a ninguém. Estornar primeiro obriga a
             * dizer o que aconteceu com o valor recebido.
             */
            return Problema("id", "Lançamento já baixado",
                $"Este lançamento foi baixado em {lancamento.PagoEm:dd/MM/yyyy}.",
                "Estorne a baixa antes de cancelar, para o valor recebido não sumir da conta.");
        }

        if (lancamento.Situacao == SituacaoLancamento.Cancelado)
        {
            return Problema("id", "Lançamento já cancelado",
                "Este lançamento já estava cancelado.",
                "Para cobrar de novo, gere a mensalidade da competência.");
        }

        var motivo = (dados.Motivo ?? string.Empty).Trim();
        if (motivo.Length == 0)
        {
            return Problema("motivo", "Motivo não informado",
                "Cancelar tira um valor da conta do escritório sem dizer por quê.",
                "Escreva o motivo: quem olhar isto daqui a seis meses vai perguntar.");
        }

        /*
         * A cobrança do PSP sai do ar antes do cancelamento. Cancelado com o
         * boleto ainda pagável era dinheiro entrando sem baixa nenhuma. Se o PSP
         * não retira, o mais provável é o cliente ter acabado de pagar, e aí
         * nada é gravado aqui.
         */
        var naoRetirou = await Cobrancas.RetirarDoPsp(
            lancamento, asaas, configuracao.Value, registros.CreateLogger("Cobranca"), cancelamento);
        if (naoRetirou is not null) return naoRetirou;

        lancamento.Situacao = SituacaoLancamento.Cancelado;
        lancamento.MotivoDoCancelamento = motivo;
        lancamento.AtualizadoEm = DateTimeOffset.UtcNow;

        if (await Gravar(banco, cancelamento) is { } conflito) return conflito;

        return Results.Ok(Detalhar(lancamento));
    }

    private static async Task<IResult> Estornar(Guid id, NexoDbContext banco, CancellationToken cancelamento)
    {
        var lancamento = await banco.Lancamentos
            .Include(r => r.Pessoa)
            .FirstOrDefaultAsync(r => r.Id == id, cancelamento);

        if (lancamento is null) return Results.NotFound();

        if (lancamento.Situacao != SituacaoLancamento.Pago)
        {
            /*
             * Estornar só faz sentido sobre uma baixa. Sobre um cancelado
             * seria pior que inútil: ressuscitaria a cobrança e poderia colidir
             * com a mensalidade que já tivesse sido gerada no lugar dela.
             */
            return Problema("id", "Nada a estornar",
                "Este lançamento não está baixado.",
                "Estorno desfaz uma baixa. Para reabrir um cancelado, gere a mensalidade de novo.");
        }

        lancamento.Situacao = SituacaoLancamento.Aberto;
        lancamento.ValorPago = null;
        lancamento.PagoEm = null;
        lancamento.OrigemDaBaixa = string.Empty;
        lancamento.AtualizadoEm = DateTimeOffset.UtcNow;

        if (await Gravar(banco, cancelamento) is { } conflito) return conflito;

        return Results.Ok(Detalhar(lancamento));
    }

    /// <summary>
    /// Grava, e transforma conflito de concorrência em recusa legível.
    ///
    /// <para>
    /// A situação do lançamento é trava de concorrência: a gravação só passa se
    /// ela ainda for a que foi lida. Quando outra operação muda o mesmo
    /// lançamento no meio do caminho, como o aviso do PSP dando baixa enquanto
    /// alguém cancela noutra aba, a segunda é recusada em vez de passar por
    /// cima da primeira. Sem este tratamento, a recusa sairia como erro 500.
    /// </para>
    /// </summary>
    private static async Task<IResult?> Gravar(NexoDbContext banco, CancellationToken cancelamento)
    {
        try
        {
            await banco.SaveChangesAsync(cancelamento);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problema("situacao", "O lançamento mudou enquanto isto era feito",
                "Outra operação alterou este lançamento no mesmo instante, e nada foi gravado.",
                "Recarregue a lista e confira a situação antes de tentar de novo.");
        }
    }

    internal static LancamentoNaLista Detalhar(Lancamento lancamento) => new(
        lancamento.Id,
        lancamento.Pessoa!.Codigo,
        lancamento.Pessoa!.Nome,
        lancamento.Descricao,
        lancamento.CompetenciaAno,
        lancamento.CompetenciaMes,
        lancamento.Valor,
        lancamento.Vencimento,
        lancamento.Situacao,
        lancamento.ValorPago,
        lancamento.PagoEm,
        lancamento.OrigemDaBaixa,
        lancamento.MotivoDoCancelamento,
        lancamento.ParcelaNumero,
        lancamento.ParcelasTotal,
        lancamento.RenegociadoDeId,
        lancamento.CobrancaUrl);

    private static IResult Problema(string campo, string titulo, string descricao, string sugestao) =>
        Results.Json(
            new RespostaComProblemas([new Problema(campo, titulo, descricao, sugestao)]),
            statusCode: 422);
}

/// <param name="CompetenciaAno">O ano a que o serviço se refere, não o do vencimento.</param>
/// <param name="CompetenciaMes">O mês a que o serviço se refere, de 1 a 12.</param>
public record DadosDoAvulso(
    Guid PessoaId,
    string Descricao,
    decimal Valor,
    DateOnly Vencimento,
    int CompetenciaAno,
    int CompetenciaMes);

public record DadosDaBaixa(decimal ValorPago, DateOnly? PagoEm);
public record DadosDoCancelamento(string? Motivo);

public record LancamentoNaLista(
    Guid Id,
    string CodigoDaPessoa,
    string NomeDaPessoa,
    string Descricao,
    int CompetenciaAno,
    int CompetenciaMes,
    decimal Valor,
    DateOnly Vencimento,
    SituacaoLancamento Situacao,
    decimal? ValorPago,
    DateOnly? PagoEm,
    string OrigemDaBaixa,
    string MotivoDoCancelamento,
    int? ParcelaNumero,
    int? ParcelasTotal,
    Guid? RenegociadoDeId,
    string CobrancaUrl);

/// <summary>Por qual coluna a listagem de lançamentos é ordenada.</summary>
public enum OrdemDeLancamentos
{
    Vencimento = 1,
    Cliente = 2,
    Competencia = 3,
    Valor = 4,
}

/// <param name="Total">Quantos lançamentos a seleção tem, e não quantos vieram nesta página.</param>
/// <param name="TotalEmAberto">Soma do que ainda não entrou, no período — independente do filtro de situação.</param>
/// <param name="TotalVencido">Parte do aberto cujo vencimento já passou.</param>
/// <param name="TotalRecebido">Soma do que de fato entrou, e não do que era devido.</param>
public record PaginaDeLancamentos(
    List<LancamentoNaLista> Itens,
    int Total,
    int Pagina,
    int Tamanho,
    decimal TotalEmAberto,
    decimal TotalVencido,
    decimal TotalRecebido);
