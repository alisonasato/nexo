namespace Nexo.Api.Endpoints;

/// <summary>
/// O único endpoint do passo 1.
///
/// Ele não tem valor de negócio nenhum: existe para provar o contrato de ponta a
/// ponta — a API descreve a si mesma em OpenAPI, o front gera o cliente a partir
/// dessa descrição, e a resposta chega ao navegador com tipo. Enquanto esse
/// caminho não fechar, nenhuma tela vale a pena (decisão Q19).
/// </summary>
public static class Saude
{
    public static IEndpointRouteBuilder MapSaude(this IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/saude", (IHostEnvironment ambiente) => new RespostaSaude(
                Status: "ok",
                Ambiente: ambiente.EnvironmentName,
                Versao: typeof(Saude).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                Momento: DateTimeOffset.UtcNow))
            .AllowAnonymous()
            .WithTags("Saúde")
            .WithName("ConsultarSaude")
            .WithSummary("Estado da API")
            .WithDescription("Devolve ambiente, versão e horário do servidor. Usado para verificar que o contrato entre API e front está fechado.")
            .Produces<RespostaSaude>(StatusCodes.Status200OK)
            .WithOpenApi();

        return rotas;
    }
}

/// <param name="Status">Sempre "ok" enquanto a API responde.</param>
/// <param name="Ambiente">Development, Staging ou Production.</param>
/// <param name="Versao">Versão do assembly em execução.</param>
/// <param name="Momento">Horário do servidor, em UTC.</param>
public record RespostaSaude(string Status, string Ambiente, string Versao, DateTimeOffset Momento);
