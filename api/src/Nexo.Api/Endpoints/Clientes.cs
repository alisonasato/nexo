using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dados;
using Nexo.Api.Dominio;

namespace Nexo.Api.Endpoints;

/// <summary>
/// O vínculo entre uma pessoa já cadastrada e o escritório.
///
/// Criar um cliente não cria uma pessoa: aponta para uma que existe. É por isso
/// que o cadastro de pessoas veio antes — e é o que permite que o mesmo CNPJ
/// seja cliente aqui e fornecedor ali, sem dois cadastros.
/// </summary>
public static class Clientes
{
    public static IEndpointRouteBuilder MapClientes(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/clientes").WithTags("Clientes");

        grupo.MapGet("/", Listar)
            .WithName("ListarClientes")
            .WithSummary("Lista os clientes do escritório")
            .Produces<List<ClienteNaLista>>();

        grupo.MapPost("/", Criar)
            .WithName("CriarCliente")
            .WithSummary("Torna uma pessoa cliente do escritório")
            .Produces<ClienteNaLista>(StatusCodes.Status201Created)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPut("/{id:guid}", Alterar)
            .WithName("AlterarCliente")
            .WithSummary("Altera o vínculo")
            .Produces<ClienteNaLista>()
            .Produces(StatusCodes.Status404NotFound);

        return rotas;
    }

    private static async Task<IResult> Listar(
        NexoDbContext banco,
        CancellationToken cancelamento,
        [FromQuery] bool incluirInativos = false)
    {
        var consulta = banco.Clientes.AsNoTracking();
        if (!incluirInativos) consulta = consulta.Where(cliente => cliente.Ativo);

        var clientes = await consulta
            .OrderBy(cliente => cliente.Codigo)
            .Select(cliente => new ClienteNaLista(
                cliente.Id,
                cliente.Codigo,
                cliente.PessoaId,
                cliente.Pessoa!.Nome,
                cliente.Pessoa.NomeFantasia,
                cliente.Pessoa.Documento,
                cliente.RegimeTributario,
                cliente.Responsavel,
                cliente.Ativo))
            .ToListAsync(cancelamento);

        return Results.Ok(clientes);
    }

    private static async Task<IResult> Criar(
        [FromBody] DadosDeCliente dados,
        NexoDbContext banco,
        IContextoDeTenant contexto,
        CancellationToken cancelamento)
    {
        if (contexto.TenantAtual is not { } tenant) return Results.Unauthorized();

        var pessoa = await banco.Pessoas.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == dados.PessoaId, cancelamento);

        if (pessoa is null)
        {
            return Problema("pessoaId", "Vínculo inválido",
                "A pessoa informada não existe neste cadastro.",
                "Cadastre a pessoa primeiro, em Pessoas, e depois torne-a cliente.");
        }

        var jaEhCliente = await banco.Clientes.AsNoTracking()
            .AnyAsync(cliente => cliente.PessoaId == dados.PessoaId, cancelamento);

        if (jaEhCliente)
        {
            return Problema("pessoaId", "Vínculo duplicado",
                $"“{pessoa.Nome}” já é cliente do escritório.",
                "Abra o cliente existente em vez de criar um segundo vínculo.");
        }

        /*
         * Os códigos já usados são lidos aqui, sob a política de RLS: a
         * sequência é por escritório, e um tenant nunca enxerga o número do
         * outro.
         */
        var codigos = await banco.Clientes.AsNoTracking()
            .Select(cliente => cliente.Codigo)
            .ToListAsync(cancelamento);

        var novo = new Cliente
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            PessoaId = dados.PessoaId,
            Codigo = Codigos.Proximo("C-", codigos),
            RegimeTributario = dados.RegimeTributario,
            Responsavel = (dados.Responsavel ?? string.Empty).Trim(),
            Observacoes = (dados.Observacoes ?? string.Empty).Trim(),
            Ativo = true,
            CriadoEm = DateTimeOffset.UtcNow,
        };

        banco.Clientes.Add(novo);
        await banco.SaveChangesAsync(cancelamento);

        return Results.Created($"/clientes/{novo.Id}", new ClienteNaLista(
            novo.Id, novo.Codigo, pessoa.Id, pessoa.Nome, pessoa.NomeFantasia,
            pessoa.Documento, novo.RegimeTributario, novo.Responsavel, novo.Ativo));
    }

    private static async Task<IResult> Alterar(
        Guid id,
        [FromBody] DadosDeCliente dados,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var cliente = await banco.Clientes
            .Include(c => c.Pessoa)
            .FirstOrDefaultAsync(c => c.Id == id, cancelamento);

        if (cliente is null) return Results.NotFound();

        cliente.RegimeTributario = dados.RegimeTributario;
        cliente.Responsavel = (dados.Responsavel ?? string.Empty).Trim();
        cliente.Observacoes = (dados.Observacoes ?? string.Empty).Trim();
        cliente.Ativo = dados.Ativo;

        await banco.SaveChangesAsync(cancelamento);

        return Results.Ok(new ClienteNaLista(
            cliente.Id, cliente.Codigo, cliente.PessoaId, cliente.Pessoa!.Nome,
            cliente.Pessoa.NomeFantasia, cliente.Pessoa.Documento,
            cliente.RegimeTributario, cliente.Responsavel, cliente.Ativo));
    }

    private static IResult Problema(string campo, string titulo, string descricao, string sugestao) =>
        Results.Json(
            new RespostaComProblemas([new Problema(campo, titulo, descricao, sugestao)]),
            statusCode: 422);
}

public record DadosDeCliente(
    Guid PessoaId,
    RegimeTributario RegimeTributario,
    string? Responsavel,
    string? Observacoes,
    bool Ativo);

public record ClienteNaLista(
    Guid Id,
    string Codigo,
    Guid PessoaId,
    string Nome,
    string NomeFantasia,
    string Documento,
    RegimeTributario RegimeTributario,
    string Responsavel,
    bool Ativo);
