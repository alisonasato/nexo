using System.Net;
using System.Text.Json.Serialization;
using Nexo.Api.Dominio;

namespace Nexo.Api.Servicos;

/// <param name="Situacao">
/// A situação cadastral na Receita: ATIVA, BAIXADA, SUSPENSA, INAPTA. Vem junto
/// porque cadastrar um cliente com empresa baixada é erro caro, e quem digita o
/// CNPJ à mão não tem como saber.
/// </param>
public record EmpresaDoCnpj(
    string Cnpj,
    string RazaoSocial,
    string NomeFantasia,
    string Situacao,
    string Logradouro,
    string Numero,
    string Complemento,
    string Bairro,
    string Cep,
    string Cidade,
    string Uf);

public record RespostaDoCnpj(ResultadoDaConsulta Resultado, EmpresaDoCnpj? Empresa = null);

public interface IConsultaDeCnpj
{
    Task<RespostaDoCnpj> BuscarAsync(string cnpj, CancellationToken cancelamento);
}

/// <summary>
/// Consulta de CNPJ na BrasilAPI, que repassa o cadastro da Receita.
///
/// <para>
/// Vale a mesma regra do CEP: é atalho de digitação, nada depende dele, e falhar
/// não impede cadastrar. A diferença é que <b>aqui o limite de requisições dói</b>
/// — é API pública e gratuita, e responder 429 é o comportamento normal dela num
/// dia movimentado, não uma exceção. Por isso o 429 tem desfecho próprio: a tela
/// precisa dizer "espere um pouco", que é verdade, e não "está fora do ar", que
/// faria desistir do atalho.
/// </para>
/// </summary>
public sealed class ConsultaDeCnpjBrasilApi(HttpClient http, ILogger<ConsultaDeCnpjBrasilApi> registro)
    : IConsultaDeCnpj
{
    public async Task<RespostaDoCnpj> BuscarAsync(string cnpj, CancellationToken cancelamento)
    {
        var numero = Documento.ApenasDigitos(cnpj);
        if (numero.Length != 14) return new RespostaDoCnpj(ResultadoDaConsulta.NaoEncontrado);

        try
        {
            var resposta = await http.GetAsync($"api/cnpj/v1/{numero}", cancelamento);

            if (resposta.StatusCode == HttpStatusCode.TooManyRequests)
                return new RespostaDoCnpj(ResultadoDaConsulta.LimiteAtingido);

            if (resposta.StatusCode == HttpStatusCode.NotFound)
                return new RespostaDoCnpj(ResultadoDaConsulta.NaoEncontrado);

            if (!resposta.IsSuccessStatusCode)
            {
                registro.LogWarning(
                    "A BrasilAPI respondeu {Codigo} para uma consulta de CNPJ.", (int)resposta.StatusCode);
                return new RespostaDoCnpj(ResultadoDaConsulta.Indisponivel);
            }

            var corpo = await resposta.Content.ReadFromJsonAsync<CorpoDaBrasilApi>(cancelamento);

            if (corpo is null || string.IsNullOrWhiteSpace(corpo.RazaoSocial))
                return new RespostaDoCnpj(ResultadoDaConsulta.NaoEncontrado);

            return new RespostaDoCnpj(ResultadoDaConsulta.Encontrado, new EmpresaDoCnpj(
                numero,
                corpo.RazaoSocial,
                corpo.NomeFantasia ?? string.Empty,
                corpo.Situacao ?? string.Empty,
                corpo.Logradouro ?? string.Empty,
                corpo.Numero ?? string.Empty,
                corpo.Complemento ?? string.Empty,
                corpo.Bairro ?? string.Empty,
                Documento.ApenasDigitos(corpo.Cep),
                corpo.Municipio ?? string.Empty,
                corpo.Uf ?? string.Empty));
        }
        catch (Exception erro) when (erro is HttpRequestException or TaskCanceledException)
        {
            registro.LogWarning(erro, "Não foi possível consultar o CNPJ.");
            return new RespostaDoCnpj(ResultadoDaConsulta.Indisponivel);
        }
    }

    /*
     * Os nomes vêm em snake_case e são mapeados um a um, em vez de ligar uma
     * política de nomes no serializador inteiro: a política valeria também para
     * o nosso contrato, que é camelCase, e trocaria o formato de toda a API por
     * causa de um serviço de fora.
     */
    private sealed record CorpoDaBrasilApi
    {
        [JsonPropertyName("razao_social")] public string? RazaoSocial { get; init; }
        [JsonPropertyName("nome_fantasia")] public string? NomeFantasia { get; init; }
        [JsonPropertyName("descricao_situacao_cadastral")] public string? Situacao { get; init; }
        [JsonPropertyName("logradouro")] public string? Logradouro { get; init; }
        [JsonPropertyName("numero")] public string? Numero { get; init; }
        [JsonPropertyName("complemento")] public string? Complemento { get; init; }
        [JsonPropertyName("bairro")] public string? Bairro { get; init; }
        [JsonPropertyName("cep")] public string? Cep { get; init; }
        [JsonPropertyName("municipio")] public string? Municipio { get; init; }
        [JsonPropertyName("uf")] public string? Uf { get; init; }
    }
}
