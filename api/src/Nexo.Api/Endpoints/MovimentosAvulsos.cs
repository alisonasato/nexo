using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dados;
using Nexo.Api.Dominio;

namespace Nexo.Api.Endpoints;

/// <summary>
/// O extrato de uma conta, e o que entra nele sem ser lançamento: tarifa,
/// rendimento, outras entradas e saídas, e transferência entre contas.
///
/// <para>
/// Sem isto o saldo nunca bate com o banco. O extrato do banco tem a tarifa do
/// mês e o rendimento da aplicação, e nenhum dos dois é conta a pagar ou a
/// receber de alguém.
/// </para>
/// </summary>
public static class MovimentosAvulsos
{
    private const int MaximoDeDias = 366;

    public static IEndpointRouteBuilder MapMovimentosAvulsos(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/contas-bancarias").WithTags("Contas bancárias");

        grupo.MapGet("/{id:guid}/extrato", Extrato)
            .WithName("ExtratoDaContaBancaria")
            .WithSummary("O extrato de uma conta num período, com o saldo linha a linha")
            .WithDescription("Sem datas, vai do primeiro dia do mês até hoje. O período nunca começa antes do saldo inicial da conta.")
            .Produces<ExtratoDaConta>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapPost("/{id:guid}/movimentos", LancarAvulso)
            .WithName("LancarMovimentoAvulso")
            .WithSummary("Lança uma tarifa, um rendimento ou outra entrada ou saída")
            .WithDescription("O valor vai sem sinal: o tipo diz se entra ou sai.")
            .Produces<MovimentoGravado>(StatusCodes.Status201Created)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapDelete("/{id:guid}/movimentos/{movimentoId:guid}", Apagar)
            .WithName("ApagarMovimento")
            .WithSummary("Apaga um movimento avulso ou uma transferência")
            .WithDescription("A transferência sai com as duas pontas. Movimento de baixa não se apaga por aqui: quem o desfaz é o estorno.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapPost("/transferencias", Transferir)
            .WithName("TransferirEntreContas")
            .WithSummary("Transfere dinheiro de uma conta do escritório para outra")
            .WithDescription("Grava as duas pontas juntas: a saída na origem e a entrada no destino.")
            .Produces<TransferenciaFeita>(StatusCodes.Status201Created)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity);

        return rotas;
    }

    /// <summary>
    /// O extrato de uma conta.
    ///
    /// <para>
    /// O saldo do começo vem somado no banco, e o saldo de cada linha é ele mais
    /// os movimentos até ali, na ordem do extrato. É uma soma corrida, e não um
    /// total: cada linha precisa do saldo da anterior.
    /// </para>
    /// <para>
    /// <b>O período nunca começa antes do saldo inicial.</b> Antes dele a conta
    /// não existe aqui, e o saldo daquele trecho seria um número inventado.
    /// </para>
    /// </summary>
    private static async Task<IResult> Extrato(
        Guid id,
        NexoDbContext banco,
        CancellationToken cancelamento,
        [FromQuery] DateOnly? de = null,
        [FromQuery] DateOnly? ate = null)
    {
        var conta = await banco.ContasBancarias.AsNoTracking()
            .FirstOrDefaultAsync(conta => conta.Id == id, cancelamento);
        if (conta is null) return Results.NotFound();

        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var inicio = de ?? new DateOnly(hoje.Year, hoje.Month, 1);
        if (inicio < conta.SaldoInicialEm) inicio = conta.SaldoInicialEm;

        var fim = ate ?? hoje;
        if (fim < inicio)
        {
            if (ate is not null)
            {
                return Recusa("ate", "Período invertido",
                    $"O período termina em {fim:dd/MM/yyyy}, antes de começar, em {inicio:dd/MM/yyyy}.",
                    "Confira as datas. O extrato não começa antes do saldo inicial da conta.");
            }

            /* Conta que começa no futuro: sem data pedida, o extrato é o dia em que ela começa. */
            fim = inicio;
        }

        var dias = fim.DayNumber - inicio.DayNumber + 1;
        if (dias > MaximoDeDias)
        {
            return Recusa("ate", "Período longo demais", $"O período tem {dias} dias.", "Use até um ano de cada vez.");
        }

        var movimentadoAntes = await banco.MovimentosDeConta.AsNoTracking()
            .Where(movimento => movimento.ContaId == id && movimento.Data < inicio)
            .SumAsync(movimento => (decimal?)movimento.Valor, cancelamento) ?? 0m;

        var saldoNoInicio = conta.SaldoInicial + movimentadoAntes;

        var linhas = await banco.MovimentosDeConta.AsNoTracking()
            .Where(movimento => movimento.ContaId == id && movimento.Data >= inicio && movimento.Data <= fim)
            .OrderBy(movimento => movimento.Data)
            .ThenBy(movimento => movimento.CriadoEm)
            .ThenBy(movimento => movimento.Id)
            .Select(movimento => new
            {
                movimento.Id,
                movimento.Data,
                movimento.Descricao,
                movimento.Valor,
                movimento.Origem,
                movimento.LancamentoId,
                movimento.TransferenciaId,
            })
            .ToListAsync(cancelamento);

        var saldo = saldoNoInicio;
        var movimentos = new List<MovimentoNoExtrato>(linhas.Count);

        foreach (var linha in linhas)
        {
            saldo += linha.Valor;
            movimentos.Add(new MovimentoNoExtrato(
                linha.Id, linha.Data, linha.Descricao, linha.Valor, linha.Origem,
                linha.LancamentoId, linha.TransferenciaId, saldo,
                OrigensDeMovimento.Avulsas.Contains(linha.Origem)));
        }

        return Results.Ok(new ExtratoDaConta(conta.Id, conta.Nome, inicio, fim, saldoNoInicio, movimentos, saldo));
    }

    private static async Task<IResult> LancarAvulso(
        Guid id,
        [FromBody] DadosDoMovimentoAvulso dados,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var existe = await banco.ContasBancarias.AnyAsync(conta => conta.Id == id, cancelamento);
        if (!existe) return Results.NotFound();

        var problemas = new List<Problema>();

        if (!Enum.IsDefined(dados.Tipo))
        {
            problemas.Add(new Problema("tipo", "Tipo não informado",
                "Não foi dito se é tarifa, rendimento, outra entrada ou outra saída.",
                "Escolha o tipo do movimento."));
        }

        problemas.AddRange(ConferirValor(dados.Valor));

        var descricao = (dados.Descricao ?? string.Empty).Trim();

        if (descricao.Length == 0 && dados.Tipo is TipoDeMovimentoAvulso.OutraEntrada or TipoDeMovimentoAvulso.OutraSaida)
        {
            problemas.Add(new Problema("descricao", "Descrição não informada",
                "Uma entrada ou saída avulsa sem descrição vira, no extrato, uma linha que ninguém explica.",
                "Diga o que foi: “Aporte do sócio”, “Devolução de caução”."));
        }
        else if (descricao.Length > 200)
        {
            problemas.Add(new Problema("descricao", "Descrição longa demais",
                $"A descrição tem {descricao.Length} caracteres.",
                "Use até 200."));
        }

        ContaBancaria? conta = null;

        if (dados.Data is not { } data)
        {
            problemas.Add(new Problema("data", "Data não informada",
                "Não foi dito em que dia o dinheiro entrou ou saiu.",
                "Informe a data como está no extrato do banco."));
        }
        else
        {
            Problema? recusa;
            (conta, recusa) = await ContasBancarias.ConferirContaDoMovimento(id, data, "contaId", "data", banco, cancelamento);
            if (recusa is not null) problemas.Add(recusa);
        }

        if (problemas.Count > 0) return Results.UnprocessableEntity(new RespostaComProblemas(problemas));

        var (origem, sinal, padrao) = dados.Tipo switch
        {
            TipoDeMovimentoAvulso.Tarifa => (OrigensDeMovimento.Tarifa, -1m, "Tarifa bancária"),
            TipoDeMovimentoAvulso.Rendimento => (OrigensDeMovimento.Rendimento, 1m, "Rendimento"),
            TipoDeMovimentoAvulso.OutraEntrada => (OrigensDeMovimento.EntradaAvulsa, 1m, string.Empty),
            _ => (OrigensDeMovimento.SaidaAvulsa, -1m, string.Empty),
        };

        var movimento = new MovimentoDeConta
        {
            Id = Guid.NewGuid(),
            TenantId = conta!.TenantId,
            ContaId = conta.Id,
            Data = dados.Data!.Value,
            Valor = sinal * decimal.Round(dados.Valor, 2, MidpointRounding.AwayFromZero),
            Descricao = descricao.Length > 0 ? descricao : padrao,
            Origem = origem,
            CriadoEm = DateTimeOffset.UtcNow,
        };

        banco.MovimentosDeConta.Add(movimento);
        await banco.SaveChangesAsync(cancelamento);

        return Results.Created($"/contas-bancarias/{id}/movimentos/{movimento.Id}",
            new MovimentoGravado(movimento.Id, movimento.Data, movimento.Descricao, movimento.Valor, movimento.Origem));
    }

    /// <summary>
    /// Apaga um movimento avulso, ou as duas pontas de uma transferência.
    ///
    /// <para>
    /// É o mesmo gesto do estorno feito à mão: corrige um registro que não devia
    /// existir. Movimento de baixa fica de fora, porque apagá-lo deixaria o
    /// lançamento pago sem dinheiro nenhum entrando; quem o desfaz é o estorno,
    /// que reabre o lançamento junto.
    /// </para>
    /// </summary>
    private static async Task<IResult> Apagar(
        Guid id,
        Guid movimentoId,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var movimento = await banco.MovimentosDeConta
            .FirstOrDefaultAsync(movimento => movimento.Id == movimentoId && movimento.ContaId == id, cancelamento);
        if (movimento is null) return Results.NotFound();

        if (!OrigensDeMovimento.Avulsas.Contains(movimento.Origem))
        {
            return Recusa("movimentoId", "Movimento de baixa",
                "Este movimento veio da baixa de um lançamento, e sai junto com ela.",
                "Para desfazê-lo, estorne a baixa em Lançamentos.");
        }

        /* As duas pontas saem juntas: uma sozinha seria dinheiro aparecendo do nada na outra conta. */
        var pontas = movimento.TransferenciaId is { } transferencia
            ? await banco.MovimentosDeConta.Where(ponta => ponta.TransferenciaId == transferencia).ToListAsync(cancelamento)
            : new List<MovimentoDeConta> { movimento };

        banco.MovimentosDeConta.RemoveRange(pontas);
        await banco.SaveChangesAsync(cancelamento);

        return Results.NoContent();
    }

    /// <summary>
    /// Transfere entre duas contas do escritório.
    ///
    /// <para>
    /// <b>As duas pontas numa gravação só.</b> A saída sem a entrada seria
    /// dinheiro sumindo; a entrada sem a saída, dinheiro aparecendo. Cada ponta
    /// passa pelas mesmas travas da baixa: conta ativa, e data a partir do saldo
    /// inicial dela.
    /// </para>
    /// </summary>
    private static async Task<IResult> Transferir(
        [FromBody] DadosDaTransferencia dados,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var problemas = new List<Problema>();
        problemas.AddRange(ConferirValor(dados.Valor));

        if (dados.OrigemId != Guid.Empty && dados.OrigemId == dados.DestinoId)
        {
            problemas.Add(new Problema("destinoId", "Mesma conta",
                "A transferência sairia e entraria na mesma conta.",
                "Escolha outra conta de destino."));
        }

        var descricao = (dados.Descricao ?? string.Empty).Trim();

        /* Cabe com o nome da outra conta, que entra na descrição de cada ponta. */
        if (descricao.Length > 130)
        {
            problemas.Add(new Problema("descricao", "Descrição longa demais",
                $"A descrição tem {descricao.Length} caracteres.",
                "Use até 130."));
        }

        ContaBancaria? origem = null;
        ContaBancaria? destino = null;

        if (dados.Data is not { } data)
        {
            problemas.Add(new Problema("data", "Data não informada",
                "Não foi dito em que dia o dinheiro mudou de conta.",
                "Informe a data como está no extrato."));
        }
        else
        {
            Problema? recusa;

            (origem, recusa) = await ContasBancarias.ConferirContaDoMovimento(
                dados.OrigemId, data, "origemId", "data", banco, cancelamento);
            if (recusa is not null) problemas.Add(recusa);

            (destino, recusa) = await ContasBancarias.ConferirContaDoMovimento(
                dados.DestinoId, data, "destinoId", "data", banco, cancelamento);
            if (recusa is not null) problemas.Add(recusa);
        }

        if (problemas.Count > 0) return Results.UnprocessableEntity(new RespostaComProblemas(problemas));

        var transferencia = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow;
        var valor = decimal.Round(dados.Valor, 2, MidpointRounding.AwayFromZero);
        var texto = descricao.Length > 0 ? descricao : "Transferência";

        var saida = new MovimentoDeConta
        {
            Id = Guid.NewGuid(),
            TenantId = origem!.TenantId,
            ContaId = origem.Id,
            TransferenciaId = transferencia,
            Data = dados.Data!.Value,
            Valor = -valor,
            Descricao = $"{texto} para {destino!.Nome}",
            Origem = OrigensDeMovimento.Transferencia,
            CriadoEm = agora,
        };

        var entrada = new MovimentoDeConta
        {
            Id = Guid.NewGuid(),
            TenantId = destino.TenantId,
            ContaId = destino.Id,
            TransferenciaId = transferencia,
            Data = dados.Data!.Value,
            Valor = valor,
            Descricao = $"{texto} de {origem.Nome}",
            Origem = OrigensDeMovimento.Transferencia,
            CriadoEm = agora,
        };

        banco.MovimentosDeConta.AddRange(saida, entrada);
        await banco.SaveChangesAsync(cancelamento);

        return Results.Json(new TransferenciaFeita(transferencia, saida.Id, entrada.Id),
            statusCode: StatusCodes.Status201Created);
    }

    private static IEnumerable<Problema> ConferirValor(decimal valor)
    {
        if (valor <= 0)
        {
            yield return new Problema("valor", "Valor inválido",
                "O valor precisa ser maior que zero.",
                "Informe o valor sem sinal: o tipo já diz de que lado o dinheiro anda.");
        }
        else if (valor >= 1_000_000_000_000m)
        {
            yield return new Problema("valor", "Valor inválido",
                "O valor é grande demais para um movimento.",
                "Confira se não entraram dígitos a mais.");
        }
    }

    private static IResult Recusa(string campo, string titulo, string descricao, string sugestao) =>
        Results.UnprocessableEntity(new RespostaComProblemas([new Problema(campo, titulo, descricao, sugestao)]));
}

public enum TipoDeMovimentoAvulso
{
    Tarifa = 1,
    Rendimento = 2,
    OutraEntrada = 3,
    OutraSaida = 4,
}

/// <param name="Valor">Sem sinal: o tipo diz se entra ou sai.</param>
/// <param name="Descricao">Obrigatória nas outras entradas e saídas. Tarifa e rendimento têm descrição padrão.</param>
public record DadosDoMovimentoAvulso(TipoDeMovimentoAvulso Tipo, DateOnly? Data, decimal Valor, string? Descricao);

public record MovimentoGravado(Guid Id, DateOnly Data, string Descricao, decimal Valor, string Origem);

/// <param name="Valor">Sem sinal: sai da origem e entra no destino.</param>
/// <param name="Descricao">Opcional, até 130 caracteres. Cada ponta leva o nome da outra conta junto.</param>
public record DadosDaTransferencia(Guid OrigemId, Guid DestinoId, DateOnly? Data, decimal Valor, string? Descricao);

public record TransferenciaFeita(Guid TransferenciaId, Guid SaidaId, Guid EntradaId);

/// <param name="SaldoDepois">O saldo da conta logo depois deste movimento, na ordem do extrato.</param>
/// <param name="PodeApagar">Avulsos e transferências se apagam pelo extrato; movimento de baixa sai pelo estorno.</param>
public record MovimentoNoExtrato(
    Guid Id,
    DateOnly Data,
    string Descricao,
    decimal Valor,
    string Origem,
    Guid? LancamentoId,
    Guid? TransferenciaId,
    decimal SaldoDepois,
    bool PodeApagar);

/// <param name="De">O começo efetivo do extrato: nunca antes do saldo inicial da conta.</param>
/// <param name="SaldoNoInicio">O saldo no começo do primeiro dia, somado no banco.</param>
public record ExtratoDaConta(
    Guid ContaId,
    string Nome,
    DateOnly De,
    DateOnly Ate,
    decimal SaldoNoInicio,
    List<MovimentoNoExtrato> Movimentos,
    decimal SaldoNoFim);
