using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dados;
using Nexo.Api.Dominio;
using Npgsql;

namespace Nexo.Api.Endpoints;

/// <summary>
/// As contas onde o dinheiro do escritório fica: bancos, a conta do PSP e o caixa.
/// </summary>
public static class ContasBancarias
{
    private const string FalhaNaValidacao = "Falha na validação da conta";

    /* O índice parcial que deixa uma conta só receber as cobranças do PSP. */
    private const string IndiceDaContaDoPsp = "ix_contas_bancarias_tenant_id";

    public static IEndpointRouteBuilder MapContasBancarias(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/contas-bancarias").WithTags("Contas bancárias");

        grupo.MapGet("/", Listar)
            .WithName("ListarContasBancarias")
            .WithSummary("Lista as contas do escritório, com o saldo")
            .WithDescription("As ativas primeiro, e dentro de cada grupo pelo nome. O saldo atual é o inicial mais os movimentos, somado no banco. Um escritório tem poucas contas: a lista vem inteira, sem página.")
            .Produces<List<ContaNaLista>>();

        grupo.MapPost("/", Criar)
            .WithName("CriarContaBancaria")
            .WithSummary("Cadastra uma conta")
            .Produces<ContaNaLista>(StatusCodes.Status201Created)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPut("/{id:guid}", Alterar)
            .WithName("AlterarContaBancaria")
            .WithSummary("Altera uma conta")
            .WithDescription("Também inativa, reativa e marca a conta que recebe as cobranças do PSP. Conta não se apaga, e o saldo inicial não muda depois do primeiro movimento.")
            .Produces<ContaNaLista>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        return rotas;
    }

    private static async Task<IResult> Listar(NexoDbContext banco, CancellationToken cancelamento)
    {
        var contas = await Resumir(banco, banco.ContasBancarias.AsNoTracking()
                .OrderByDescending(conta => conta.Ativa)
                .ThenBy(conta => conta.Nome)
                .ThenBy(conta => conta.Id))
            .ToListAsync(cancelamento);

        return Results.Ok(contas.Select(Detalhar).ToList());
    }

    private static async Task<IResult> Criar(
        [FromBody] DadosDaConta dados,
        NexoDbContext banco,
        IContextoDeTenant contexto,
        CancellationToken cancelamento)
    {
        if (contexto.TenantAtual is not { } tenant) return Results.Unauthorized();

        /* Conta nasce ativa: cadastrar uma conta já encerrada não tem para quê. */
        var problemas = await Conferir(dados, atual: null, ativa: true, temMovimentos: false, banco, cancelamento);
        if (problemas.Count > 0) return Results.UnprocessableEntity(new RespostaComProblemas(problemas));

        var agora = DateTimeOffset.UtcNow;
        var conta = new ContaBancaria { Id = Guid.NewGuid(), TenantId = tenant, CriadoEm = agora };

        Aplicar(conta, dados, agora);
        conta.Ativa = true;

        banco.ContasBancarias.Add(conta);
        if (await GravarComAMarcaDoPsp(banco, conta, cancelamento) is { } recusa) return recusa;

        return Results.Created($"/contas-bancarias/{conta.Id}", Detalhar(new ContaResumida(conta, 0m, false)));
    }

    /// <summary>
    /// Altera, inativa e reativa. Id de outro escritório não é encontrado,
    /// porque a política de isolamento não o enxerga, e a resposta vira 404.
    /// </summary>
    private static async Task<IResult> Alterar(
        Guid id,
        [FromBody] DadosDaConta dados,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var conta = await banco.ContasBancarias.FirstOrDefaultAsync(conta => conta.Id == id, cancelamento);
        if (conta is null) return Results.NotFound();

        var temMovimentos = await banco.MovimentosDeConta.AnyAsync(movimento => movimento.ContaId == id, cancelamento);

        var problemas = await Conferir(dados, conta, dados.Ativa, temMovimentos, banco, cancelamento);
        if (problemas.Count > 0) return Results.UnprocessableEntity(new RespostaComProblemas(problemas));

        Aplicar(conta, dados, DateTimeOffset.UtcNow);
        conta.Ativa = dados.Ativa;

        if (await GravarComAMarcaDoPsp(banco, conta, cancelamento) is { } recusa) return recusa;

        var resumida = await Resumir(banco, banco.ContasBancarias.AsNoTracking().Where(outra => outra.Id == id))
            .SingleAsync(cancelamento);

        return Results.Ok(Detalhar(resumida));
    }

    /// <summary>
    /// A conta de um movimento: informada, deste escritório, ativa, e começando
    /// antes do dinheiro. Serve à baixa, ao movimento avulso e às duas pontas de
    /// uma transferência.
    ///
    /// <para>
    /// <b>Data antes do saldo inicial é recusada.</b> O dinheiro de antes daquele
    /// dia já está dentro do saldo inicial, e um movimento anterior a ele contaria
    /// o mesmo dinheiro duas vezes.
    /// </para>
    /// </summary>
    /// <param name="campoDaConta">Para onde a recusa da conta aponta: "contaId" na baixa, "origemId" ou "destinoId" na transferência.</param>
    /// <param name="campoDaData">Para onde a recusa da data aponta: "pagoEm" na baixa, "data" no movimento.</param>
    internal static async Task<(ContaBancaria? Conta, Problema? Recusa)> ConferirContaDoMovimento(
        Guid contaId,
        DateOnly data,
        string campoDaConta,
        string campoDaData,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        if (contaId == Guid.Empty)
        {
            return (null, new Problema(campoDaConta, "Conta não informada",
                "Não foi dito em que conta o dinheiro entrou ou saiu.",
                "Escolha a conta."));
        }

        var conta = await banco.ContasBancarias.AsNoTracking()
            .FirstOrDefaultAsync(conta => conta.Id == contaId, cancelamento);

        if (conta is null)
        {
            return (null, new Problema(campoDaConta, "Conta não encontrada",
                "A conta informada não existe neste escritório.",
                "Escolha uma conta da lista."));
        }

        if (!conta.Ativa)
        {
            return (null, new Problema(campoDaConta, "Conta inativa",
                $"A conta “{conta.Nome}” está inativa.",
                "Escolha uma conta ativa, ou reative esta em Contas bancárias."));
        }

        if (data < conta.SaldoInicialEm)
        {
            return (null, new Problema(campoDaData, "Data antes do saldo inicial",
                $"A conta “{conta.Nome}” começa com o saldo de {conta.SaldoInicialEm:dd/MM/yyyy}, e a data informada é {data:dd/MM/yyyy}.",
                "O dinheiro de antes daquele dia já está no saldo inicial. Confira a data ou a conta."));
        }

        return (conta, null);
    }

    /// <param name="atual">A conta sendo alterada; nula ao cadastrar.</param>
    /// <param name="ativa">Como a conta vai ficar: ao cadastrar é sempre ativa, seja o que for que veio no corpo.</param>
    private static async Task<List<Problema>> Conferir(
        DadosDaConta dados,
        ContaBancaria? atual,
        bool ativa,
        bool temMovimentos,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var problemas = new List<Problema>();
        var nome = dados.Nome?.Trim() ?? string.Empty;
        var propria = atual?.Id ?? Guid.Empty;

        if (nome.Length == 0)
        {
            problemas.Add(new Problema("nome", FalhaNaValidacao,
                "O nome da conta não foi informado.",
                "Use o nome com que o escritório fala dela: “Itaú PJ”, “Asaas”, “Caixa da recepção”."));
        }
        else if (nome.Length > 60)
        {
            problemas.Add(new Problema("nome", FalhaNaValidacao,
                $"O nome tem {nome.Length} caracteres.",
                "Use até 60."));
        }
        else
        {
            /*
             * Sem diferenciar maiúscula: "Caixa" e "caixa" seriam duas contas
             * que ninguém distingue na hora de escolher onde o dinheiro entrou.
             */
            var minusculo = nome.ToLower();
            var repetida = await banco.ContasBancarias.AnyAsync(
                conta => conta.Id != propria && conta.Nome.ToLower() == minusculo, cancelamento);

            if (repetida)
            {
                problemas.Add(new Problema("nome", "Conta já cadastrada",
                    $"Já existe uma conta chamada “{nome}”.",
                    "Dê um nome que diferencie as duas, como o banco ou o final do número."));
            }
        }

        if (!Enum.IsDefined(dados.Tipo))
        {
            problemas.Add(new Problema("tipo", FalhaNaValidacao,
                "O tipo da conta não foi informado.",
                "Escolha entre conta corrente, poupança, conta de pagamento e dinheiro em caixa."));
        }

        if (dados.SaldoInicialEm is null)
        {
            problemas.Add(new Problema("saldoInicialEm", FalhaNaValidacao,
                "Não foi dito em que dia o saldo inicial valia.",
                "Informe a data do saldo como está no extrato: o saldo no começo daquele dia."));
        }

        /* O teto da coluna, 14 dígitos com 2 casas. Acima disso o banco recusaria com erro 500. */
        if (Math.Abs(dados.SaldoInicial) >= 1_000_000_000_000m)
        {
            problemas.Add(new Problema("saldoInicial", FalhaNaValidacao,
                "O saldo inicial é grande demais para ser um saldo.",
                "Confira se não entraram dígitos a mais."));
        }

        /*
         * Depois do primeiro movimento, o saldo inicial está por baixo de todos
         * os saldos desde então. Mudá-lo refaria a história da conta em
         * silêncio, e o saldo de hoje deixaria de bater com o extrato sem
         * nenhuma linha nova que explicasse.
         */
        if (atual is not null && temMovimentos
            && (decimal.Round(dados.SaldoInicial, 2, MidpointRounding.AwayFromZero) != atual.SaldoInicial
                || (dados.SaldoInicialEm is { } dia && dia != atual.SaldoInicialEm)))
        {
            problemas.Add(new Problema("saldoInicial", "A conta já tem movimentos",
                "Mudar o saldo inicial agora mudaria o saldo de todos os dias desde então.",
                "Se o saldo inicial estava errado, estorne as baixas desta conta, corrija o saldo e baixe de novo."));
        }

        if (dados.RecebeCobrancas && !ativa)
        {
            problemas.Add(new Problema("recebeCobrancas", "Conta inativa não recebe cobranças",
                "Uma conta encerrada não tem como receber o que o PSP cobrar.",
                "Marque outra conta para receber as cobranças antes de inativar esta."));
        }

        foreach (var (campo, valor, limite) in new[]
                 {
                     ("banco", dados.Banco, 60),
                     ("agencia", dados.Agencia, 10),
                     ("numero", dados.Numero, 20),
                 })
        {
            if ((valor?.Trim().Length ?? 0) > limite)
            {
                problemas.Add(new Problema(campo, FalhaNaValidacao,
                    $"O campo passou de {limite} caracteres.",
                    "Confira se não entrou ali o texto de outro campo."));
            }
        }

        return problemas;
    }

    private static void Aplicar(ContaBancaria conta, DadosDaConta dados, DateTimeOffset agora)
    {
        conta.Nome = dados.Nome!.Trim();
        conta.Tipo = dados.Tipo;

        /* Caixa em dinheiro não tem banco: guardar o que veio seria guardar um erro de digitação. */
        var emDinheiro = dados.Tipo == TipoConta.Dinheiro;
        conta.Banco = emDinheiro ? string.Empty : dados.Banco?.Trim() ?? string.Empty;
        conta.Agencia = emDinheiro ? string.Empty : dados.Agencia?.Trim() ?? string.Empty;
        conta.Numero = emDinheiro ? string.Empty : dados.Numero?.Trim() ?? string.Empty;

        conta.SaldoInicial = decimal.Round(dados.SaldoInicial, 2, MidpointRounding.AwayFromZero);
        conta.SaldoInicialEm = dados.SaldoInicialEm!.Value;
        conta.RecebeCobrancas = dados.RecebeCobrancas;
        conta.AtualizadoEm = agora;
    }

    /// <summary>
    /// Grava a conta e, quando ela passa a receber as cobranças, tira a marca da
    /// que recebia antes, na mesma transação.
    ///
    /// <para>
    /// <b>Marcar outra conta move a marca, em vez de recusar.</b> Trocar a conta
    /// do PSP é um gesto só para quem faz. Recusar obrigaria a desmarcar uma
    /// antes, com um intervalo em que nenhuma recebe e a cobrança é recusada; a
    /// transação faz esse intervalo não existir para mais ninguém.
    /// </para>
    /// </summary>
    private static async Task<IResult?> GravarComAMarcaDoPsp(
        NexoDbContext banco,
        ContaBancaria conta,
        CancellationToken cancelamento)
    {
        var estrategia = banco.Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await banco.Database.BeginTransactionAsync(cancelamento);

            if (conta.RecebeCobrancas)
            {
                await banco.ContasBancarias
                    .Where(outra => outra.Id != conta.Id && outra.RecebeCobrancas)
                    .ExecuteUpdateAsync(ajuste => ajuste.SetProperty(outra => outra.RecebeCobrancas, false), cancelamento);
            }

            if (await Gravar(banco, cancelamento) is { } recusa) return recusa;

            await transacao.CommitAsync(cancelamento);
            return (IResult?)null;
        });
    }

    /// <summary>
    /// Grava, e transforma a repetição que escapou da conferência em recusa
    /// legível. Escapa quando duas pessoas gravam no mesmo instante: a
    /// conferência passa para as duas, e o índice único recusa a segunda.
    /// </summary>
    private static async Task<IResult?> Gravar(NexoDbContext banco, CancellationToken cancelamento)
    {
        try
        {
            await banco.SaveChangesAsync(cancelamento);
            return null;
        }
        catch (DbUpdateException erro) when (erro.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } violacao)
        {
            var problema = violacao.ConstraintName == IndiceDaContaDoPsp
                ? new Problema("recebeCobrancas", "Outra conta recebe as cobranças",
                    "Outra conta foi marcada para receber as cobranças no mesmo instante.",
                    "Recarregue a lista e confira qual conta ficou marcada.")
                : new Problema("nome", "Conta já cadastrada",
                    "Outra conta com este nome foi gravada no mesmo instante.",
                    "Recarregue a lista e escolha outro nome.");

            return Results.UnprocessableEntity(new RespostaComProblemas([problema]));
        }
    }

    /// <summary>A conta com o que só o banco sabe dela: quanto se movimentou e se já se movimentou.</summary>
    private sealed record ContaResumida(ContaBancaria Conta, decimal Movimentado, bool TemMovimentos);

    /// <summary>
    /// Soma os movimentos de cada conta no banco. Trazer os movimentos para
    /// somar aqui daria o mesmo número hoje e um número lento, e depois errado,
    /// no dia em que alguém limitasse a consulta.
    /// </summary>
    private static IQueryable<ContaResumida> Resumir(NexoDbContext banco, IQueryable<ContaBancaria> contas) =>
        contas.Select(conta => new ContaResumida(
            conta,
            banco.MovimentosDeConta.Where(movimento => movimento.ContaId == conta.Id)
                .Sum(movimento => (decimal?)movimento.Valor) ?? 0m,
            banco.MovimentosDeConta.Any(movimento => movimento.ContaId == conta.Id)));

    private static ContaNaLista Detalhar(ContaResumida resumida) => new(
        resumida.Conta.Id,
        resumida.Conta.Nome,
        resumida.Conta.Tipo,
        resumida.Conta.Banco,
        resumida.Conta.Agencia,
        resumida.Conta.Numero,
        resumida.Conta.SaldoInicial,
        resumida.Conta.SaldoInicialEm,
        resumida.Conta.SaldoInicial + resumida.Movimentado,
        resumida.Conta.Ativa,
        resumida.Conta.RecebeCobrancas,
        resumida.TemMovimentos);
}

/// <param name="SaldoInicial">O saldo no começo do dia informado. Pode ser negativo.</param>
/// <param name="SaldoInicialEm">O dia em cujo começo o saldo inicial valia.</param>
/// <param name="Ativa">Ignorado ao cadastrar: conta nasce ativa.</param>
/// <param name="RecebeCobrancas">A conta onde entra o que o PSP cobra. Só uma por escritório: marcar esta tira a marca da outra.</param>
public record DadosDaConta(
    string? Nome,
    TipoConta Tipo,
    string? Banco,
    string? Agencia,
    string? Numero,
    decimal SaldoInicial,
    DateOnly? SaldoInicialEm,
    bool Ativa = true,
    bool RecebeCobrancas = false);

/// <param name="SaldoAtual">O saldo inicial mais todos os movimentos da conta, somados no banco.</param>
/// <param name="TemMovimentos">Com movimentos, o saldo inicial já não se altera.</param>
public record ContaNaLista(
    Guid Id,
    string Nome,
    TipoConta Tipo,
    string Banco,
    string Agencia,
    string Numero,
    decimal SaldoInicial,
    DateOnly SaldoInicialEm,
    decimal SaldoAtual,
    bool Ativa,
    bool RecebeCobrancas,
    bool TemMovimentos);
