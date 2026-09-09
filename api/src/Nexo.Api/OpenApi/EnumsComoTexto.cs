using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Nexo.Api.OpenApi;

/// <summary>
/// Descreve os enums no documento como texto, que é o que de fato trafega.
///
/// O <c>JsonStringEnumConverter</c> muda a serialização em tempo de execução,
/// mas o Swashbuckle monta o esquema a partir do tipo CLR e continuaria
/// anunciando <c>integer</c>. As duas coisas discordando é pior do que
/// qualquer uma das duas sozinha: o cliente gerado esperaria número, receberia
/// texto, e o erro só apareceria no navegador de alguém.
/// </summary>
public sealed class EnumsComoTexto : ISchemaFilter
{
    public void Apply(OpenApiSchema esquema, SchemaFilterContext contexto)
    {
        if (!contexto.Type.IsEnum) return;

        esquema.Type = "string";
        esquema.Format = null;
        esquema.Enum = Enum.GetNames(contexto.Type)
            .Select(nome => (IOpenApiAny)new OpenApiString(nome))
            .ToList();
    }
}
