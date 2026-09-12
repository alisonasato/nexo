using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dados;
using Nexo.Api.Dominio;

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

        rotas.MapPut("/empresas/{id:guid}", Alterar)
            .WithTags("Empresas")
            .WithName("AlterarEmpresa")
            .WithSummary("Corrige os dados do estabelecimento")
            .WithDescription("O provisionamento grava razão social e CNPJ uma vez, a partir de variável de ambiente. Sem isto, um dígito errado ali não teria conserto pela aplicação.")
            .Produces<EmpresaNaLista>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi();

        return rotas;
    }

    /// <summary>
    /// Corrige o estabelecimento.
    ///
    /// <para>
    /// Não há <c>POST</c> ao lado deste <c>PUT</c>, e a falta é de propósito.
    /// Abrir filial não é preencher um formulário: mexe em qual empresa vai no
    /// token, em como o usuário troca de estabelecimento e no que acontece com
    /// o dado já gravado. Enquanto essas perguntas não tiverem resposta, criar
    /// empresa pela tela produziria linha órfã que ninguém alcança.
    /// </para>
    /// <para>
    /// A consulta não filtra por tenant, como nenhuma outra aqui: id de outro
    /// escritório não é encontrado porque a política de RLS não o enxerga, e a
    /// resposta vira 404 sozinha.
    /// </para>
    /// </summary>
    private static async Task<IResult> Alterar(
        Guid id,
        DadosDeEmpresa dados,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var empresa = await banco.Empresas.FirstOrDefaultAsync(empresa => empresa.Id == id, cancelamento);
        if (empresa is null) return Results.NotFound();

        var cnpj = Documento.NormalizarCnpj(dados.Cnpj);

        /*
         * O CNPJ é único dentro do escritório, e a busca exclui a própria
         * empresa: sem isso, salvar sem mexer no documento acusaria duplicidade
         * consigo mesma.
         */
        var outra = cnpj.Length == 14
            ? await banco.Empresas.FirstOrDefaultAsync(
                outra => outra.Id != id && outra.Cnpj == cnpj, cancelamento)
            : null;

        var candidata = new Empresa
        {
            RazaoSocial = dados.RazaoSocial?.Trim() ?? string.Empty,
            NomeFantasia = dados.NomeFantasia?.Trim(),
            Cnpj = cnpj,
        };

        var problemas = ValidadorDeEmpresa.Validar(candidata, outra);
        if (problemas.Count > 0)
        {
            return Results.UnprocessableEntity(new RespostaComProblemas(problemas));
        }

        empresa.RazaoSocial = candidata.RazaoSocial;
        empresa.NomeFantasia = candidata.NomeFantasia;
        empresa.Cnpj = candidata.Cnpj;

        await banco.SaveChangesAsync(cancelamento);

        return Results.Ok(new EmpresaNaLista(
            empresa.Id, empresa.RazaoSocial, empresa.NomeFantasia, empresa.Cnpj,
            empresa.MatrizId == null));
    }
}

public record DadosDeEmpresa(string? RazaoSocial, string? NomeFantasia, string? Cnpj);

/// <param name="EhMatriz">Verdadeiro quando a empresa não é filial de outra.</param>
public record EmpresaNaLista(Guid Id, string RazaoSocial, string? NomeFantasia, string Cnpj, bool EhMatriz);
