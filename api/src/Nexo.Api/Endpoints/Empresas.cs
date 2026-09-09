using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dados;

namespace Nexo.Api.Endpoints;

/// <summary>
/// A primeira leitura de dado de negócio protegida por sessão.
///
/// Repare no que <b>não</b> existe na consulta: filtro por tenant. Não há
/// <c>Where(e => e.TenantId == ...)</c> em lugar nenhum. Quem filtra é a
/// política de RLS no Postgres, alimentada pela claim do token — e é
/// exatamente esse o desenho da decisão Q22: o dia em que alguém escrever uma
/// consulta nova e esquecer o filtro, o banco continua recusando.
/// </summary>
public static class Empresas
{
    public static IEndpointRouteBuilder MapEmpresas(this IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/empresas", async (NexoDbContext banco, CancellationToken cancelamento) =>
            {
                var empresas = await banco.Empresas
                    .OrderBy(empresa => empresa.RazaoSocial)
                    .Select(empresa => new EmpresaNaLista(
                        empresa.Id,
                        empresa.RazaoSocial,
                        empresa.NomeFantasia,
                        empresa.Cnpj,
                        empresa.MatrizId == null))
                    .ToListAsync(cancelamento);

                return Results.Ok(empresas);
            })
            .WithTags("Empresas")
            .WithName("ListarEmpresas")
            .WithSummary("Empresas do tenant da sessão")
            .Produces<List<EmpresaNaLista>>()
            .WithOpenApi();

        return rotas;
    }
}

/// <param name="EhMatriz">Verdadeiro quando a empresa não é filial de outra.</param>
public record EmpresaNaLista(Guid Id, string RazaoSocial, string? NomeFantasia, string Cnpj, bool EhMatriz);
