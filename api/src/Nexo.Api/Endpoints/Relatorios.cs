using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dados;
using Nexo.Api.Dominio;

namespace Nexo.Api.Endpoints;

/// <summary>
/// O fechamento por categoria: quanto entrou e saiu em cada uma, mês a mês.
/// </summary>
public static class Relatorios
{
    /// <summary>Dois anos. Além disso a tabela deixa de caber na tela e de responder alguma coisa.</summary>
    private const int MaximoDeMeses = 24;

    /// <summary>Meio ano, que é o período em que uma tendência aparece sem virar planilha.</summary>
    private const int MesesPadrao = 6;

    public static IEndpointRouteBuilder MapRelatorios(this IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/relatorios/por-categoria", PorCategoria)
            .WithTags("Relatórios")
            .WithName("RelatorioPorCategoria")
            .WithSummary("O fechamento por categoria, mês a mês")
            .WithDescription("Por competência, soma o valor dos lançamentos do mês a que eles se referem, pagos ou não. Por caixa, soma o valor pago no mês em que o dinheiro se moveu. Cancelado e renegociado ficam de fora dos dois. Sem período, são os últimos seis meses.")
            .Produces<RelatorioDeCategorias>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity);

        return rotas;
    }

    /// <summary>
    /// O fechamento por categoria de um período.
    ///
    /// <para>
    /// <b>Competência e caixa respondem perguntas diferentes, e a resposta diz
    /// qual está respondendo.</b> Competência é o mês a que o serviço se refere,
    /// pago ou não: é o fechamento contábil. Caixa é o mês em que o dinheiro se
    /// moveu, pelo valor que de fato entrou ou saiu. O mesmo honorário de março,
    /// pago em abril, aparece em março num e em abril no outro.
    /// </para>
    /// <para>
    /// <b>Renegociado fica de fora, junto com cancelado.</b> O título renegociado
    /// continua no banco, e as parcelas que o substituem também: contar os dois
    /// somaria o mesmo dinheiro duas vezes, e o relatório passaria a mentir
    /// exatamente no mês em que alguém renegociou.
    /// </para>
    /// <para>
    /// <b>Movimento de conta não entra.</b> Tarifa, rendimento e aporte não têm
    /// categoria, e nem lançamento por trás; quem responde por eles é o extrato e
    /// o fluxo de caixa. Um relatório que os somasse sem poder classificá-los
    /// jogaria dinheiro numa linha "sem categoria" que ninguém consegue resolver.
    /// </para>
    /// <para>
    /// As somas saem do banco, agrupadas por categoria e mês. O que é feito aqui
    /// é o que o banco não sabe fazer sozinho: subir os valores pela árvore.
    /// </para>
    /// </summary>
    private static async Task<IResult> PorCategoria(
        NexoDbContext banco,
        CancellationToken cancelamento,
        [FromQuery] RegimeDoRelatorio regime = RegimeDoRelatorio.Competencia,
        [FromQuery] int? deAno = null,
        [FromQuery] int? deMes = null,
        [FromQuery] int? ateAno = null,
        [FromQuery] int? ateMes = null,
        [FromQuery] Guid? centroDeCustoId = null,
        [FromQuery] bool semCentroDeCusto = false)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);

        var anoDoFim = ateAno ?? hoje.Year;
        var mesDoFim = ateMes ?? hoje.Month;
        if (Invalida(anoDoFim, mesDoFim) is { } fimRecusado) return fimRecusado;

        /* Sem início, os seis meses que terminam no fim escolhido. */
        var padrao = new DateOnly(anoDoFim, mesDoFim, 1).AddMonths(-(MesesPadrao - 1));
        var anoDoInicio = deAno ?? padrao.Year;
        var mesDoInicio = deMes ?? padrao.Month;
        if (Invalida(anoDoInicio, mesDoInicio) is { } inicioRecusado) return inicioRecusado;

        var doInicio = new DateOnly(anoDoInicio, mesDoInicio, 1);
        var doFim = new DateOnly(anoDoFim, mesDoFim, 1);

        if (doFim < doInicio)
        {
            return Recusa("ate", "Período invertido",
                $"O período termina em {mesDoFim:00}/{anoDoFim}, antes de começar, em {mesDoInicio:00}/{anoDoInicio}.",
                "Troque as competências de lugar.");
        }

        var quantidade = (anoDoFim * 12 + mesDoFim) - (anoDoInicio * 12 + mesDoInicio) + 1;
        if (quantidade > MaximoDeMeses)
        {
            return Recusa("ate", "Período longo demais",
                $"O período tem {quantidade} meses.",
                $"Use até {MaximoDeMeses} meses de cada vez.");
        }

        IQueryable<Lancamento> consulta = banco.Lancamentos.AsNoTracking()
            .Where(lancamento => lancamento.Situacao != SituacaoLancamento.Cancelado
                && lancamento.Situacao != SituacaoLancamento.Renegociado);

        if (semCentroDeCusto)
        {
            consulta = consulta.Where(lancamento => lancamento.CentroDeCustoId == null);
        }
        else if (centroDeCustoId is { } centro)
        {
            consulta = consulta.Where(lancamento => lancamento.CentroDeCustoId == centro);
        }

        var ultimoDia = doFim.AddMonths(1).AddDays(-1);

        IQueryable<SomaDoMes> somas = regime == RegimeDoRelatorio.Caixa
            ? consulta
                .Where(lancamento => lancamento.Situacao == SituacaoLancamento.Pago
                    && lancamento.PagoEm >= doInicio && lancamento.PagoEm <= ultimoDia)
                .GroupBy(lancamento => new
                {
                    lancamento.Natureza,
                    lancamento.CategoriaId,
                    Ano = lancamento.PagoEm!.Value.Year,
                    Mes = lancamento.PagoEm!.Value.Month,
                })
                .Select(grupo => new SomaDoMes(grupo.Key.Natureza, grupo.Key.CategoriaId, grupo.Key.Ano, grupo.Key.Mes,
                    grupo.Sum(lancamento => lancamento.ValorPago ?? 0m)))
            : consulta
                .Where(lancamento =>
                    lancamento.CompetenciaAno * 12 + lancamento.CompetenciaMes >= anoDoInicio * 12 + mesDoInicio
                    && lancamento.CompetenciaAno * 12 + lancamento.CompetenciaMes <= anoDoFim * 12 + mesDoFim)
                .GroupBy(lancamento => new
                {
                    lancamento.Natureza,
                    lancamento.CategoriaId,
                    Ano = lancamento.CompetenciaAno,
                    Mes = lancamento.CompetenciaMes,
                })
                .Select(grupo => new SomaDoMes(grupo.Key.Natureza, grupo.Key.CategoriaId, grupo.Key.Ano, grupo.Key.Mes,
                    grupo.Sum(lancamento => lancamento.Valor)));

        var lidas = await somas.ToListAsync(cancelamento);

        var categorias = PlanoDeContas.EmArvore(await banco.Categorias.AsNoTracking().ToListAsync(cancelamento));

        var meses = Enumerable.Range(0, quantidade)
            .Select(passo => doInicio.AddMonths(passo))
            .Select(data => new CompetenciaDoRelatorio(data.Year, data.Month))
            .ToList();

        var receitas = Montar(NaturezaLancamento.Receber, categorias, lidas, meses);
        var despesas = Montar(NaturezaLancamento.Pagar, categorias, lidas, meses);

        var resultado = Enumerable.Range(0, meses.Count)
            .Select(coluna => receitas.Totais[coluna] - despesas.Totais[coluna])
            .ToList();

        return Results.Ok(new RelatorioDeCategorias(
            regime, meses, [receitas, despesas], resultado, resultado.Sum()));
    }

    /// <summary>
    /// As linhas de uma natureza, na ordem da árvore.
    ///
    /// <para>
    /// <b>Cada categoria mostra o que é dela mais o que caiu nas de baixo</b>, que
    /// é o que um plano em árvore quer dizer: "Pessoal" responde por salários e
    /// encargos. Por isso o total do grupo soma só as de cima — somar todas as
    /// linhas contaria o mesmo dinheiro uma vez por nível.
    /// </para>
    /// <para>
    /// Só entram as categorias com valor no período. Um plano sugerido inteiro,
    /// com dezenas de linhas zeradas, esconderia as poucas que têm dinheiro.
    /// </para>
    /// </summary>
    private static GrupoDoRelatorio Montar(
        NaturezaLancamento natureza,
        List<CategoriaNaLista> categorias,
        List<SomaDoMes> somas,
        List<CompetenciaDoRelatorio> meses)
    {
        var colunaDoMes = meses
            .Select((mes, posicao) => (mes, posicao))
            .ToDictionary(item => (item.mes.Ano, item.mes.Mes), item => item.posicao);

        /* O que caiu em cada categoria, sem as de baixo, e à parte o que ficou sem classificar. */
        var proprios = new Dictionary<Guid, decimal[]>();
        var semCategoria = new decimal[meses.Count];

        foreach (var soma in somas.Where(soma => soma.Natureza == natureza))
        {
            if (!colunaDoMes.TryGetValue((soma.Ano, soma.Mes), out var coluna)) continue;

            if (soma.CategoriaId is not { } categoria)
            {
                semCategoria[coluna] += soma.Soma;
                continue;
            }

            if (!proprios.TryGetValue(categoria, out var valores))
            {
                valores = new decimal[meses.Count];
                proprios[categoria] = valores;
            }

            valores[coluna] += soma.Soma;
        }

        var daNatureza = categorias.Where(categoria => categoria.Natureza == natureza).ToList();

        var acumulados = daNatureza.ToDictionary(
            categoria => categoria.Id,
            categoria => proprios.TryGetValue(categoria.Id, out var proprio)
                ? (decimal[])proprio.Clone()
                : new decimal[meses.Count]);

        /*
         * A ordem de árvore põe a filha depois da mãe. Percorrida de trás para
         * frente, cada categoria já está somada quando chega a hora de entregar o
         * valor dela para a de cima.
         */
        foreach (var categoria in Enumerable.Reverse(daNatureza))
        {
            if (categoria.PaiId is not { } pai || !acumulados.TryGetValue(pai, out var acima)) continue;

            var proprio = acumulados[categoria.Id];
            for (var coluna = 0; coluna < meses.Count; coluna++) acima[coluna] += proprio[coluna];
        }

        var linhas = new List<LinhaDoRelatorio>();

        foreach (var categoria in daNatureza)
        {
            var valores = acumulados[categoria.Id];
            if (valores.All(valor => valor == 0m)) continue;

            linhas.Add(new LinhaDoRelatorio(
                categoria.Id, categoria.Nome, categoria.Nivel, [.. valores], valores.Sum()));
        }

        if (semCategoria.Any(valor => valor != 0m))
        {
            linhas.Add(new LinhaDoRelatorio(null, "Sem categoria", 1, [.. semCategoria], semCategoria.Sum()));
        }

        var totais = new decimal[meses.Count];

        foreach (var linha in linhas.Where(linha => linha.Nivel == 1))
        {
            for (var coluna = 0; coluna < meses.Count; coluna++) totais[coluna] += linha.Valores[coluna];
        }

        return new GrupoDoRelatorio(natureza, linhas, [.. totais], totais.Sum());
    }

    private static IResult? Invalida(int ano, int mes)
    {
        if (mes is < 1 or > 12)
        {
            return Recusa("mes", "Competência inválida",
                $"O mês informado foi {mes}.",
                "Informe um mês entre 1 e 12.");
        }

        if (ano is < 2000 or > 2100)
        {
            return Recusa("ano", "Competência inválida",
                $"O ano informado foi {ano}.",
                "Informe um ano entre 2000 e 2100.");
        }

        return null;
    }

    private static IResult Recusa(string campo, string titulo, string descricao, string sugestao) =>
        Results.Json(new RespostaComProblemas([new Problema(campo, titulo, descricao, sugestao)]), statusCode: 422);
}

/// <summary>Que pergunta o relatório responde.</summary>
public enum RegimeDoRelatorio
{
    /// <summary>O mês a que o lançamento se refere, pago ou não.</summary>
    Competencia = 1,

    /// <summary>O mês em que o dinheiro se moveu, pelo valor pago.</summary>
    Caixa = 2,
}

/// <summary>Uma soma vinda do banco: uma categoria, num mês, de uma natureza.</summary>
internal record SomaDoMes(NaturezaLancamento Natureza, Guid? CategoriaId, int Ano, int Mes, decimal Soma);

public record CompetenciaDoRelatorio(int Ano, int Mes);

/// <param name="CategoriaId">Nulo na linha do que ficou sem classificar.</param>
/// <param name="Nivel">1 na raiz, até 3. A linha "Sem categoria" vem no nível 1.</param>
/// <param name="Valores">Um valor por mês, na mesma ordem de <c>Meses</c>. Inclui as categorias de baixo.</param>
public record LinhaDoRelatorio(Guid? CategoriaId, string Categoria, int Nivel, List<decimal> Valores, decimal Total);

/// <param name="Totais">A soma das categorias de cima, mês a mês: as de baixo já estão dentro delas.</param>
public record GrupoDoRelatorio(
    NaturezaLancamento Natureza,
    List<LinhaDoRelatorio> Linhas,
    List<decimal> Totais,
    decimal Total);

/// <param name="Grupos">A receber primeiro, a pagar depois.</param>
/// <param name="Resultado">Receitas menos despesas, mês a mês.</param>
public record RelatorioDeCategorias(
    RegimeDoRelatorio Regime,
    List<CompetenciaDoRelatorio> Meses,
    List<GrupoDoRelatorio> Grupos,
    List<decimal> Resultado,
    decimal ResultadoTotal);
