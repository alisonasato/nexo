using Nexo.Api.Dominio;
using Nexo.Api.Servicos;

namespace Nexo.Api.Endpoints;

/// <summary>
/// Atalhos de digitação que perguntam a serviços de fora.
///
/// <para>
/// Estão juntos sob <c>/consultas</c> de propósito: não são recursos do Nexo,
/// não guardam nada e não decidem nada. É digitação poupada, e o cadastro
/// funciona igual com todos eles fora do ar.
/// </para>
/// <para>
/// Por isso cada desfecho tem código próprio. A tela precisa dizer coisas
/// diferentes para "não existe", "fora do ar" e "espere um pouco" — e a última
/// é comum, porque API pública e gratuita limita requisição.
/// </para>
/// </summary>
public static class Consultas
{
    public static IEndpointRouteBuilder MapConsultas(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/consultas").WithTags("Consultas");

        grupo.MapGet("/cep/{cep}", BuscarCep)
            .WithName("ConsultarCep")
            .WithSummary("Procura o endereço de um CEP")
            .WithDescription("Serve para preencher o formulário, e nada depende dele: 503 quer dizer para digitar à mão, não que algo deu errado no cadastro.")
            .Produces<EnderecoDoCep>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        grupo.MapGet("/cnpj/{cnpj}", BuscarCnpj)
            .WithName("ConsultarCnpj")
            .WithSummary("Procura razão social e endereço de um CNPJ")
            .WithDescription("O CNPJ vai só com os dígitos: a barra da forma escrita seria outro trecho do endereço. Traz junto a situação cadastral na Receita, porque cadastrar cliente com empresa baixada é erro caro e quem digita à mão não tem como saber.")
            .Produces<EmpresaDoCnpj>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        return rotas;
    }

    private static async Task<IResult> BuscarCep(
        string cep,
        IConsultaDeCep consulta,
        CancellationToken cancelamento)
    {
        if (Documento.ApenasDigitos(cep).Length != 8)
        {
            return Recusa("cep", "CEP inválido",
                "O CEP precisa ter oito dígitos.",
                "Confira o número: são oito dígitos, com ou sem o hífen.");
        }

        var resposta = await consulta.BuscarAsync(cep, cancelamento);

        return resposta.Resultado switch
        {
            ResultadoDaConsulta.Encontrado => Results.Ok(resposta.Endereco),
            ResultadoDaConsulta.NaoEncontrado => Results.NotFound(),
            ResultadoDaConsulta.LimiteAtingido => Results.StatusCode(StatusCodes.Status429TooManyRequests),
            _ => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
        };
    }

    private static async Task<IResult> BuscarCnpj(
        string cnpj,
        IConsultaDeCnpj consulta,
        CancellationToken cancelamento)
    {
        var numero = Documento.ApenasDigitos(cnpj);

        if (numero.Length != 14)
        {
            return Recusa("cnpj", "CNPJ inválido",
                "O CNPJ precisa ter quatorze dígitos.",
                "Informe os quatorze dígitos. A barra do CNPJ formatado não cabe num " +
                "endereço, então a pontuação completa não serve aqui.");
        }

        /*
         * Os dígitos verificadores são conferidos aqui, antes de sair.
         * Consultar um CNPJ que já se sabe inválido gasta uma requisição de um
         * limite que é escasso, e volta com a resposta errada: "não existe",
         * quando o certo é "está digitado errado".
         */
        if (!Documento.CnpjValido(numero))
        {
            return Recusa("cnpj", "CNPJ inválido",
                "Os dígitos verificadores não conferem.",
                "Confira o número digitado antes de consultar.");
        }

        var resposta = await consulta.BuscarAsync(numero, cancelamento);

        return resposta.Resultado switch
        {
            ResultadoDaConsulta.Encontrado => Results.Ok(resposta.Empresa),
            ResultadoDaConsulta.NaoEncontrado => Results.NotFound(),
            ResultadoDaConsulta.LimiteAtingido => Results.StatusCode(StatusCodes.Status429TooManyRequests),
            _ => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
        };
    }

    private static IResult Recusa(string campo, string titulo, string descricao, string sugestao) =>
        Results.Json(
            new RespostaComProblemas([new Problema(campo, titulo, descricao, sugestao)]),
            statusCode: 422);
}
