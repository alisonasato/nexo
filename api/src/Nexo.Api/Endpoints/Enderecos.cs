using Nexo.Api.Dominio;
using Nexo.Api.Servicos;

namespace Nexo.Api.Endpoints;

/// <summary>
/// Atalhos de digitação para endereço.
///
/// Nada aqui guarda dado nem decide coisa alguma: é digitação poupada. O
/// cadastro funciona igual com o serviço fora do ar, e é por isso que a falha
/// tem código próprio em vez de virar erro genérico — a tela precisa saber a
/// diferença entre "este CEP não existe" e "não deu para perguntar agora".
/// </summary>
public static class Enderecos
{
    public static IEndpointRouteBuilder MapEnderecos(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/enderecos").WithTags("Endereços");

        grupo.MapGet("/{cep}", Buscar)
            .WithName("BuscarEnderecoPorCep")
            .WithSummary("Procura o endereço de um CEP")
            .WithDescription("Serve para preencher o formulário, e nada depende dele: 503 quer dizer para digitar à mão, não que algo deu errado no cadastro.")
            .Produces<EnderecoDoCep>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        return rotas;
    }

    private static async Task<IResult> Buscar(
        string cep,
        IConsultaDeCep consulta,
        CancellationToken cancelamento)
    {
        if (Documento.ApenasDigitos(cep).Length != 8)
        {
            return Results.Json(
                new RespostaComProblemas([new Problema(
                    "cep", "CEP inválido",
                    "O CEP precisa ter oito dígitos.",
                    "Confira o número: são oito dígitos, com ou sem o hífen.")]),
                statusCode: 422);
        }

        var resposta = await consulta.BuscarAsync(cep, cancelamento);

        return resposta.Resultado switch
        {
            ResultadoDaConsulta.Encontrado => Results.Ok(resposta.Endereco),
            ResultadoDaConsulta.NaoEncontrado => Results.NotFound(),
            _ => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
        };
    }
}
