using System.Reflection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Nexo.Api.OpenApi;

/// <summary>
/// Marca como obrigatória, no documento OpenAPI, toda propriedade que o C# já
/// garante não ser nula.
///
/// Sem isto o documento sai com tudo opcional, e o cliente TypeScript gerado a
/// partir dele enche o front de `| undefined` que não existe na realidade —
/// obrigando a checar nulo em campo que nunca é nulo. O
/// <c>SupportNonNullableReferenceTypes</c> do Swashbuckle cuida do
/// <c>nullable</c>, mas não alimenta o <c>required</c>.
///
/// A nulidade é lida do próprio CLR com <see cref="NullabilityInfoContext"/>,
/// e não da configuração do Swashbuckle, para que a resposta seja a mesma que
/// o compilador dá.
/// </summary>
public sealed class PropriedadesNaoAnulaveisSaoObrigatorias : ISchemaFilter
{
    public void Apply(OpenApiSchema esquema, SchemaFilterContext contexto)
    {
        if (esquema.Properties is null || esquema.Properties.Count == 0) return;

        var nulidade = new NullabilityInfoContext();
        var propriedades = contexto.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var nome in esquema.Properties.Keys)
        {
            // O esquema usa camelCase e o CLR usa PascalCase.
            var propriedade = propriedades.FirstOrDefault(
                p => string.Equals(p.Name, nome, StringComparison.OrdinalIgnoreCase));

            if (propriedade is null) continue;

            var tipo = propriedade.PropertyType;
            var obrigatoria = tipo.IsValueType
                ? Nullable.GetUnderlyingType(tipo) is null
                : nulidade.Create(propriedade).ReadState == NullabilityState.NotNull;

            if (obrigatoria && !esquema.Required.Contains(nome))
                esquema.Required.Add(nome);
        }
    }
}
