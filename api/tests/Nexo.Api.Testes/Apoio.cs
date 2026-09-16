using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// O que quase todo teste de borda repetia: o JSON com enum por nome, o campo
/// recusado numa resposta 422, e uma pessoa com papel para pendurar contrato ou
/// lançamento.
/// </summary>
internal static class Apoio
{
    /// <summary>
    /// Enum vai e volta por nome, como na API. Com o padrão do System.Text.Json,
    /// <c>"Pagar"</c> viraria 2 no corpo, e o teste passaria a falar outra língua
    /// que a do contrato.
    /// </summary>
    public static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>O campo do único problema de uma recusa. Falha antes disso se a resposta não for 422.</summary>
    public static async Task<string> CampoRecusado(HttpResponseMessage resposta)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>(Json);
        return Assert.Single(corpo!.Problemas).Campo;
    }

    /// <summary>Uma pessoa jurídica nova, com o papel pedido e documento válido.</summary>
    public static async Task<Guid> CriarPessoa(HttpClient http, Papel papel, string nome = "Pessoa do teste")
    {
        var resposta = await http.PostAsJsonAsync("/pessoas", new DadosDePessoa(
            TipoPessoa.Juridica, RegimeTributario.SimplesNacional, string.Empty, [papel],
            nome, string.Empty, Documentos.CnpjValido(), string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, null, string.Empty, true), Json);

        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<PessoaDetalhada>(Json))!.Id;
    }
}
