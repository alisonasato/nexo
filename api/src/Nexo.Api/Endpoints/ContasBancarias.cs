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

    public static IEndpointRouteBuilder MapContasBancarias(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/contas-bancarias").WithTags("Contas bancárias");

        grupo.MapGet("/", Listar)
            .WithName("ListarContasBancarias")
            .WithSummary("Lista as contas do escritório")
            .WithDescription("As ativas primeiro, e dentro de cada grupo pelo nome. Um escritório tem poucas contas: a lista vem inteira, sem página.")
            .Produces<List<ContaNaLista>>();

        grupo.MapPost("/", Criar)
            .WithName("CriarContaBancaria")
            .WithSummary("Cadastra uma conta")
            .Produces<ContaNaLista>(StatusCodes.Status201Created)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPut("/{id:guid}", Alterar)
            .WithName("AlterarContaBancaria")
            .WithSummary("Altera uma conta")
            .WithDescription("Também inativa e reativa. Conta não se apaga: o dinheiro que passou por ela continua precisando dela.")
            .Produces<ContaNaLista>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        return rotas;
    }

    private static async Task<IResult> Listar(NexoDbContext banco, CancellationToken cancelamento)
    {
        var contas = await banco.ContasBancarias.AsNoTracking()
            .OrderByDescending(conta => conta.Ativa)
            .ThenBy(conta => conta.Nome)
            .ThenBy(conta => conta.Id)
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

        var problemas = await Conferir(dados, Guid.Empty, banco, cancelamento);
        if (problemas.Count > 0) return Results.UnprocessableEntity(new RespostaComProblemas(problemas));

        var agora = DateTimeOffset.UtcNow;
        var conta = new ContaBancaria { Id = Guid.NewGuid(), TenantId = tenant, CriadoEm = agora };

        Aplicar(conta, dados, agora);

        /* Conta nasce ativa: cadastrar uma conta já encerrada não tem para quê. */
        conta.Ativa = true;

        banco.ContasBancarias.Add(conta);
        if (await Gravar(banco, cancelamento) is { } recusa) return recusa;

        return Results.Created($"/contas-bancarias/{conta.Id}", Detalhar(conta));
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

        var problemas = await Conferir(dados, id, banco, cancelamento);
        if (problemas.Count > 0) return Results.UnprocessableEntity(new RespostaComProblemas(problemas));

        Aplicar(conta, dados, DateTimeOffset.UtcNow);
        conta.Ativa = dados.Ativa;

        if (await Gravar(banco, cancelamento) is { } recusa) return recusa;

        return Results.Ok(Detalhar(conta));
    }

    /// <param name="propria">A conta sendo alterada, fora da busca por nome repetido. Vazio ao cadastrar.</param>
    private static async Task<List<Problema>> Conferir(
        DadosDaConta dados,
        Guid propria,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var problemas = new List<Problema>();
        var nome = dados.Nome?.Trim() ?? string.Empty;

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
        conta.AtualizadoEm = agora;
    }

    /// <summary>
    /// Grava, e transforma o nome repetido que escapou da conferência em recusa
    /// legível. Escapa quando duas pessoas cadastram a mesma conta no mesmo
    /// instante: a conferência passa para as duas, e o índice único recusa a
    /// segunda.
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
        })
        {
            return Results.UnprocessableEntity(new RespostaComProblemas([
                new Problema("nome", "Conta já cadastrada",
                    "Outra conta com este nome foi gravada no mesmo instante.",
                    "Recarregue a lista e escolha outro nome."),
            ]));
        }
    }

    private static ContaNaLista Detalhar(ContaBancaria conta) => new(
        conta.Id,
        conta.Nome,
        conta.Tipo,
        conta.Banco,
        conta.Agencia,
        conta.Numero,
        conta.SaldoInicial,
        conta.SaldoInicialEm,
        conta.Ativa);
}

/// <param name="SaldoInicial">O saldo no começo do dia informado. Pode ser negativo.</param>
/// <param name="SaldoInicialEm">O dia em cujo começo o saldo inicial valia.</param>
/// <param name="Ativa">Ignorado ao cadastrar: conta nasce ativa.</param>
public record DadosDaConta(
    string? Nome,
    TipoConta Tipo,
    string? Banco,
    string? Agencia,
    string? Numero,
    decimal SaldoInicial,
    DateOnly? SaldoInicialEm,
    bool Ativa = true);

public record ContaNaLista(
    Guid Id,
    string Nome,
    TipoConta Tipo,
    string Banco,
    string Agencia,
    string Numero,
    decimal SaldoInicial,
    DateOnly SaldoInicialEm,
    bool Ativa);
