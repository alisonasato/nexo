using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dados;
using Nexo.Api.Dominio;

namespace Nexo.Api.Endpoints;

/// <summary>
/// O que o escritório tem a receber, e a baixa quando o dinheiro entra.
/// </summary>
public static class Recebiveis
{
    public static IEndpointRouteBuilder MapRecebiveis(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/recebiveis").WithTags("Recebíveis");

        grupo.MapGet("/", Listar)
            .WithName("ListarRecebiveis")
            .WithSummary("Lista os recebíveis")
            .Produces<PaginaDeRecebiveis>();

        grupo.MapPost("/", Criar)
            .WithName("CriarRecebivelAvulso")
            .WithSummary("Lança uma cobrança fora de contrato")
            .WithDescription("Para o que o escritório faz e não é mensalidade: declaração de imposto de renda, abertura de empresa, certidão. Fica sem contrato por trás.")
            .Produces<RecebivelNaLista>(StatusCodes.Status201Created)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPost("/{id:guid}/baixar", Baixar)
            .WithName("BaixarRecebivel")
            .WithSummary("Registra o recebimento")
            .Produces<RecebivelNaLista>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapPost("/{id:guid}/cancelar", Cancelar)
            .WithName("CancelarRecebivel")
            .WithSummary("Cancela um recebível em aberto")
            .WithDescription("Libera a competência para ser gerada de novo, sem apagar o histórico do que foi cancelado.")
            .Produces<RecebivelNaLista>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapPost("/{id:guid}/estornar", Estornar)
            .WithName("EstornarRecebivel")
            .WithSummary("Desfaz a baixa")
            .WithDescription("Existe porque baixa errada acontece, e sem estorno a correção viraria um segundo registro inventado.")
            .Produces<RecebivelNaLista>()
            .Produces(StatusCodes.Status404NotFound);

        return rotas;
    }

    /// <summary>
    /// Lista os recebíveis, paginados, com os totais do período.
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
        [FromQuery] SituacaoRecebivel? situacao = null,
        [FromQuery] int? ano = null,
        [FromQuery] int? mes = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 25)
    {
        pagina = Math.Max(1, pagina);
        tamanho = Math.Clamp(tamanho, 1, 200);

        /* O período: o que os totais enxergam. */
        var doPeriodo = banco.Recebiveis.AsNoTracking();
        if (ano is { } a) doPeriodo = doPeriodo.Where(r => r.CompetenciaAno == a);
        if (mes is { } m) doPeriodo = doPeriodo.Where(r => r.CompetenciaMes == m);

        /* A fatia: o que a lista mostra. */
        var daLista = situacao is { } filtro ? doPeriodo.Where(r => r.Situacao == filtro) : doPeriodo;

        var total = await daLista.CountAsync(cancelamento);

        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var emAberto = doPeriodo.Where(r => r.Situacao == SituacaoRecebivel.Aberto);

        /*
         * O molde `(decimal?)` não é enfeite: SUM de conjunto vazio devolve
         * NULL no SQL, e sem o tipo anulável o EF tenta encaixar isso num
         * decimal e estoura. Mês sem nenhum recebível é o caso mais comum de
         * todos — é o mês que ainda não começou.
         */
        var totalEmAberto = await emAberto
            .Select(r => (decimal?)r.Valor).SumAsync(cancelamento) ?? 0m;

        var totalVencido = await emAberto.Where(r => r.Vencimento < hoje)
            .Select(r => (decimal?)r.Valor).SumAsync(cancelamento) ?? 0m;

        var totalRecebido = await doPeriodo.Where(r => r.Situacao == SituacaoRecebivel.Pago)
            .Select(r => r.ValorPago).SumAsync(cancelamento) ?? 0m;

        var itens = await daLista
            .OrderBy(recebivel => recebivel.Vencimento)
            /* Tamanho antes do texto: o código do cliente é número puro, e sem
               isso o 10 viria antes do 2 dentro do mesmo vencimento. */
            .ThenBy(recebivel => recebivel.Pessoa!.Codigo.Length)
            .ThenBy(recebivel => recebivel.Pessoa!.Codigo)
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .Select(recebivel => new RecebivelNaLista(
                recebivel.Id,
                recebivel.Pessoa!.Codigo,
                recebivel.Pessoa!.Nome,
                recebivel.Descricao,
                recebivel.CompetenciaAno,
                recebivel.CompetenciaMes,
                recebivel.Valor,
                recebivel.Vencimento,
                recebivel.Situacao,
                recebivel.ValorPago,
                recebivel.PagoEm,
                recebivel.OrigemDaBaixa,
                recebivel.MotivoDoCancelamento))
            .ToListAsync(cancelamento);

        return Results.Ok(new PaginaDeRecebiveis(
            itens, total, pagina, tamanho, totalEmAberto, totalVencido, totalRecebido));
    }

    /// <summary>
    /// Lança um recebível sem contrato por trás.
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

        var recebivel = new Recebivel
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
            Situacao = SituacaoRecebivel.Aberto,
            CriadoEm = agora,
            AtualizadoEm = agora,
        };

        banco.Recebiveis.Add(recebivel);
        await banco.SaveChangesAsync(cancelamento);

        await banco.Entry(recebivel).Reference(r => r.Pessoa).LoadAsync(cancelamento);

        return Results.Created($"/recebiveis/{recebivel.Id}", Detalhar(recebivel));
    }

    private static async Task<List<Problema>> Conferir(
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
        CancellationToken cancelamento)
    {
        var recebivel = await banco.Recebiveis
            .Include(r => r.Pessoa)
            .FirstOrDefaultAsync(r => r.Id == id, cancelamento);

        if (recebivel is null) return Results.NotFound();

        if (recebivel.Situacao == SituacaoRecebivel.Pago)
        {
            /*
             * Recusar em vez de sobrescrever. Baixar duas vezes costuma ser
             * clique repetido, e sobrescrever apagaria a data e o valor da
             * primeira baixa — que é exatamente o que alguém vai procurar
             * quando a conciliação não fechar.
             */
            return Problema("id", "Recebível já baixado",
                $"Este recebível foi baixado em {recebivel.PagoEm:dd/MM/yyyy}.",
                "Para corrigir o valor ou a data, estorne a baixa e registre de novo.");
        }

        if (recebivel.Situacao == SituacaoRecebivel.Cancelado)
        {
            return Problema("id", "Recebível cancelado",
                "Um recebível cancelado não pode ser baixado.",
                "Se a cobrança voltou a valer, gere-a novamente.");
        }

        if (dados.ValorPago <= 0)
        {
            return Problema("valorPago", "Valor inválido",
                "O valor recebido precisa ser maior que zero.",
                "Informe quanto entrou de fato — pode ser diferente do valor cobrado.");
        }

        recebivel.Situacao = SituacaoRecebivel.Pago;
        recebivel.ValorPago = dados.ValorPago;
        recebivel.PagoEm = dados.PagoEm ?? DateOnly.FromDateTime(DateTime.Today);
        recebivel.OrigemDaBaixa = OrigensDeBaixa.Manual;
        recebivel.AtualizadoEm = DateTimeOffset.UtcNow;

        await banco.SaveChangesAsync(cancelamento);

        return Results.Ok(Detalhar(recebivel));
    }

    /// <summary>
    /// Cancela um recebível em aberto.
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
        CancellationToken cancelamento)
    {
        var recebivel = await banco.Recebiveis
            .Include(r => r.Pessoa)
            .FirstOrDefaultAsync(r => r.Id == id, cancelamento);

        if (recebivel is null) return Results.NotFound();

        if (recebivel.Situacao == SituacaoRecebivel.Pago)
        {
            /*
             * Cancelar o que já foi pago apagaria a entrada de dinheiro da
             * conta sem devolver nada a ninguém. Estornar primeiro obriga a
             * dizer o que aconteceu com o valor recebido.
             */
            return Problema("id", "Recebível já baixado",
                $"Este recebível foi baixado em {recebivel.PagoEm:dd/MM/yyyy}.",
                "Estorne a baixa antes de cancelar, para o valor recebido não sumir da conta.");
        }

        if (recebivel.Situacao == SituacaoRecebivel.Cancelado)
        {
            return Problema("id", "Recebível já cancelado",
                "Este recebível já estava cancelado.",
                "Para cobrar de novo, gere a mensalidade da competência.");
        }

        var motivo = (dados.Motivo ?? string.Empty).Trim();
        if (motivo.Length == 0)
        {
            return Problema("motivo", "Motivo não informado",
                "Cancelar tira um valor da conta do escritório sem dizer por quê.",
                "Escreva o motivo: quem olhar isto daqui a seis meses vai perguntar.");
        }

        recebivel.Situacao = SituacaoRecebivel.Cancelado;
        recebivel.MotivoDoCancelamento = motivo;
        recebivel.AtualizadoEm = DateTimeOffset.UtcNow;

        await banco.SaveChangesAsync(cancelamento);

        return Results.Ok(Detalhar(recebivel));
    }

    private static async Task<IResult> Estornar(Guid id, NexoDbContext banco, CancellationToken cancelamento)
    {
        var recebivel = await banco.Recebiveis
            .Include(r => r.Pessoa)
            .FirstOrDefaultAsync(r => r.Id == id, cancelamento);

        if (recebivel is null) return Results.NotFound();

        if (recebivel.Situacao != SituacaoRecebivel.Pago)
        {
            /*
             * Estornar só faz sentido sobre uma baixa. Sobre um cancelado
             * seria pior que inútil: ressuscitaria a cobrança e poderia colidir
             * com a mensalidade que já tivesse sido gerada no lugar dela.
             */
            return Problema("id", "Nada a estornar",
                "Este recebível não está baixado.",
                "Estorno desfaz uma baixa. Para reabrir um cancelado, gere a mensalidade de novo.");
        }

        recebivel.Situacao = SituacaoRecebivel.Aberto;
        recebivel.ValorPago = null;
        recebivel.PagoEm = null;
        recebivel.OrigemDaBaixa = string.Empty;
        recebivel.AtualizadoEm = DateTimeOffset.UtcNow;

        await banco.SaveChangesAsync(cancelamento);

        return Results.Ok(Detalhar(recebivel));
    }

    private static RecebivelNaLista Detalhar(Recebivel recebivel) => new(
        recebivel.Id,
        recebivel.Pessoa!.Codigo,
        recebivel.Pessoa!.Nome,
        recebivel.Descricao,
        recebivel.CompetenciaAno,
        recebivel.CompetenciaMes,
        recebivel.Valor,
        recebivel.Vencimento,
        recebivel.Situacao,
        recebivel.ValorPago,
        recebivel.PagoEm,
        recebivel.OrigemDaBaixa,
        recebivel.MotivoDoCancelamento);

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

public record RecebivelNaLista(
    Guid Id,
    string CodigoDaPessoa,
    string NomeDaPessoa,
    string Descricao,
    int CompetenciaAno,
    int CompetenciaMes,
    decimal Valor,
    DateOnly Vencimento,
    SituacaoRecebivel Situacao,
    decimal? ValorPago,
    DateOnly? PagoEm,
    string OrigemDaBaixa,
    string MotivoDoCancelamento);

/// <param name="Total">Quantos recebíveis a seleção tem, e não quantos vieram nesta página.</param>
/// <param name="TotalEmAberto">Soma do que ainda não entrou, no período — independente do filtro de situação.</param>
/// <param name="TotalVencido">Parte do aberto cujo vencimento já passou.</param>
/// <param name="TotalRecebido">Soma do que de fato entrou, e não do que era devido.</param>
public record PaginaDeRecebiveis(
    List<RecebivelNaLista> Itens,
    int Total,
    int Pagina,
    int Tamanho,
    decimal TotalEmAberto,
    decimal TotalVencido,
    decimal TotalRecebido);
