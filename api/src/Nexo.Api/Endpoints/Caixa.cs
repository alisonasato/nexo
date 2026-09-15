using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dados;
using Nexo.Api.Dominio;

namespace Nexo.Api.Endpoints;

/// <summary>
/// O fluxo de caixa: o que entrou e saiu das contas, e o que está para entrar e sair.
/// </summary>
public static class Caixa
{
    /// <summary>Um ano. Além disso a projeção vira palpite sobre lançamentos que ainda nem existem.</summary>
    private const int MaximoDeDias = 366;

    public static IEndpointRouteBuilder MapCaixa(this IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/fluxo-de-caixa", Calcular)
            .WithTags("Fluxo de caixa")
            .WithName("CalcularFluxoDeCaixa")
            .WithSummary("O realizado e o projetado de um período, com o saldo acumulado")
            .WithDescription("O realizado vem dos movimentos das contas; o projetado, dos lançamentos em aberto que vencem de hoje em diante. O que está em atraso vem à parte, fora do saldo projetado. Sem datas, o período vai de hoje a trinta dias adiante.")
            .Produces<FluxoNoPeriodo>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity);

        return rotas;
    }

    /// <summary>
    /// Calcula o fluxo de um período.
    ///
    /// <para>
    /// <b>O atraso fica fora da projeção.</b> Um título vencido há dois meses não
    /// vai entrar amanhã só porque a projeção precisa de uma data para ele.
    /// Somá-lo faria o caixa parecer maior do que é, justo quando a pergunta é se
    /// dá para pagar as contas da semana. Ele vem à parte, com o valor, para quem
    /// decide.
    /// </para>
    /// <para>
    /// <b>O passado é só realizado.</b> O que venceu antes de hoje e não foi pago
    /// é atraso, e não projeção de um dia que já passou.
    /// </para>
    /// <para>
    /// <b>O saldo inicial de cada conta entra no dia em que ela começa.</b> A conta
    /// que começa no meio do período não estava no saldo do começo dele, e o
    /// dinheiro dela não pode aparecer antes do dia em que passou a existir aqui.
    /// </para>
    /// <para>
    /// As somas saem do banco por dia; o agrupamento por mês e o saldo acumulado
    /// são feitos em cima delas, porque o acumulado é sequencial por natureza. O
    /// que nunca acontece é somar linha de lançamento ou de movimento aqui.
    /// </para>
    /// </summary>
    private static async Task<IResult> Calcular(
        NexoDbContext banco,
        CancellationToken cancelamento,
        [FromQuery] DateOnly? de = null,
        [FromQuery] DateOnly? ate = null,
        [FromQuery] AgrupamentoDoFluxo agrupamento = AgrupamentoDoFluxo.Dia)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var inicio = de ?? hoje;
        var fim = ate ?? inicio.AddDays(30);

        if (fim < inicio)
        {
            return Recusa("ate", "Período invertido",
                $"O período termina em {fim:dd/MM/yyyy}, antes de começar, em {inicio:dd/MM/yyyy}.",
                "Troque as datas de lugar.");
        }

        var dias = fim.DayNumber - inicio.DayNumber + 1;
        if (dias > MaximoDeDias)
        {
            return Recusa("ate", "Período longo demais",
                $"O período tem {dias} dias.",
                "Use até um ano de cada vez.");
        }

        var contas = await banco.ContasBancarias.AsNoTracking()
            .Select(conta => new { conta.SaldoInicial, conta.SaldoInicialEm })
            .ToListAsync(cancelamento);

        var movimentadoAntes = await banco.MovimentosDeConta.AsNoTracking()
            .Where(movimento => movimento.Data < inicio)
            .SumAsync(movimento => (decimal?)movimento.Valor, cancelamento) ?? 0m;

        var saldoNoInicio = contas.Where(conta => conta.SaldoInicialEm <= inicio).Sum(conta => conta.SaldoInicial)
            + movimentadoAntes;

        /*
         * Transferência fica fora do realizado: para o escritório nada entrou nem
         * saiu, o dinheiro só trocou de conta. Contá-la inflaria entradas e saídas
         * com o mesmo valor. No saldo ela se anula sozinha, e por isso segue lá.
         */
        var realizadoPorDia = await banco.MovimentosDeConta.AsNoTracking()
            .Where(movimento => movimento.Data >= inicio && movimento.Data <= fim
                && movimento.Origem != OrigensDeMovimento.Transferencia)
            .GroupBy(movimento => movimento.Data)
            .Select(dia => new
            {
                Dia = dia.Key,
                Entradas = dia.Sum(movimento => movimento.Valor > 0 ? movimento.Valor : 0m),
                Saidas = dia.Sum(movimento => movimento.Valor < 0 ? -movimento.Valor : 0m),
            })
            .ToListAsync(cancelamento);

        var projetarDe = inicio > hoje ? inicio : hoje;

        var projetadoPorDia = await banco.Lancamentos.AsNoTracking()
            .Where(lancamento => lancamento.Situacao == SituacaoLancamento.Aberto
                && lancamento.Vencimento >= projetarDe
                && lancamento.Vencimento <= fim)
            .GroupBy(lancamento => lancamento.Vencimento)
            .Select(dia => new
            {
                Dia = dia.Key,
                AReceber = dia.Sum(lancamento => lancamento.Natureza == NaturezaLancamento.Receber ? lancamento.Valor : 0m),
                APagar = dia.Sum(lancamento => lancamento.Natureza == NaturezaLancamento.Pagar ? lancamento.Valor : 0m),
            })
            .ToListAsync(cancelamento);

        var atraso = await banco.Lancamentos.AsNoTracking()
            .Where(lancamento => lancamento.Situacao == SituacaoLancamento.Aberto && lancamento.Vencimento < hoje)
            .GroupBy(lancamento => lancamento.Natureza)
            .Select(natureza => new { Natureza = natureza.Key, Total = natureza.Sum(lancamento => lancamento.Valor) })
            .ToListAsync(cancelamento);

        var periodos = new List<PeriodoDoFluxo>();
        var saldo = saldoNoInicio;

        foreach (var (comeco, termino) in Periodos(inicio, fim, agrupamento))
        {
            bool Dentro(DateOnly dia) => dia >= comeco && dia <= termino;

            var contasAbertas = contas
                .Where(conta => conta.SaldoInicialEm > inicio && Dentro(conta.SaldoInicialEm))
                .Sum(conta => conta.SaldoInicial);

            var realizado = realizadoPorDia.Where(dia => Dentro(dia.Dia)).ToList();
            var projetado = projetadoPorDia.Where(dia => Dentro(dia.Dia)).ToList();

            var periodo = new PeriodoDoFluxo(
                comeco,
                termino,
                contasAbertas,
                realizado.Sum(dia => dia.Entradas),
                realizado.Sum(dia => dia.Saidas),
                projetado.Sum(dia => dia.AReceber),
                projetado.Sum(dia => dia.APagar),
                0m);

            saldo += periodo.ContasAbertas + periodo.Entradas - periodo.Saidas + periodo.AReceber - periodo.APagar;
            periodos.Add(periodo with { Saldo = saldo });
        }

        return Results.Ok(new FluxoNoPeriodo(
            inicio,
            fim,
            agrupamento,
            hoje,
            saldoNoInicio,
            periodos.Sum(periodo => periodo.Entradas),
            periodos.Sum(periodo => periodo.Saidas),
            periodos.Sum(periodo => periodo.AReceber),
            periodos.Sum(periodo => periodo.APagar),
            atraso.Where(grupo => grupo.Natureza == NaturezaLancamento.Receber).Sum(grupo => grupo.Total),
            atraso.Where(grupo => grupo.Natureza == NaturezaLancamento.Pagar).Sum(grupo => grupo.Total),
            periodos,
            saldo));
    }

    /// <summary>
    /// Os períodos, do começo ao fim. Por mês, o primeiro começa no dia pedido e
    /// o último termina no dia pedido: o mês cortado mostra só a parte que está
    /// no período, e não o mês inteiro com um pedaço de fora.
    /// </summary>
    private static IEnumerable<(DateOnly Comeco, DateOnly Termino)> Periodos(
        DateOnly inicio, DateOnly fim, AgrupamentoDoFluxo agrupamento)
    {
        var comeco = inicio;

        while (comeco <= fim)
        {
            var termino = agrupamento == AgrupamentoDoFluxo.Mes
                ? new DateOnly(comeco.Year, comeco.Month, 1).AddMonths(1).AddDays(-1)
                : comeco;

            if (termino > fim) termino = fim;

            yield return (comeco, termino);
            comeco = termino.AddDays(1);
        }
    }

    private static IResult Recusa(string campo, string titulo, string descricao, string sugestao) =>
        Results.UnprocessableEntity(new RespostaComProblemas([new Problema(campo, titulo, descricao, sugestao)]));
}

public enum AgrupamentoDoFluxo
{
    Dia = 1,
    Mes = 2,
}

/// <param name="ContasAbertas">O saldo inicial das contas que começam dentro do período.</param>
/// <param name="Entradas">O que entrou nas contas, dos movimentos.</param>
/// <param name="Saidas">O que saiu das contas, em valor positivo.</param>
/// <param name="AReceber">Lançamentos a receber em aberto que vencem no período, de hoje em diante.</param>
/// <param name="APagar">Lançamentos a pagar em aberto que vencem no período, de hoje em diante.</param>
/// <param name="Saldo">O saldo ao fim do período, com o realizado e o projetado até ali.</param>
public record PeriodoDoFluxo(
    DateOnly Comeco,
    DateOnly Termino,
    decimal ContasAbertas,
    decimal Entradas,
    decimal Saidas,
    decimal AReceber,
    decimal APagar,
    decimal Saldo);

/// <param name="Hoje">O dia que separa o realizado do projetado, como o servidor o conta.</param>
/// <param name="SaldoNoInicio">O saldo das contas no começo do primeiro dia.</param>
/// <param name="EmAtrasoAReceber">O que venceu antes de hoje e segue em aberto. Fica fora do saldo projetado.</param>
/// <param name="EmAtrasoAPagar">O que venceu antes de hoje e segue em aberto. Fica fora do saldo projetado.</param>
/// <param name="SaldoNoFim">O saldo ao fim do último dia, com o realizado e o projetado.</param>
public record FluxoNoPeriodo(
    DateOnly De,
    DateOnly Ate,
    AgrupamentoDoFluxo Agrupamento,
    DateOnly Hoje,
    decimal SaldoNoInicio,
    decimal TotalDeEntradas,
    decimal TotalDeSaidas,
    decimal TotalAReceber,
    decimal TotalAPagar,
    decimal EmAtrasoAReceber,
    decimal EmAtrasoAPagar,
    List<PeriodoDoFluxo> Periodos,
    decimal SaldoNoFim);
