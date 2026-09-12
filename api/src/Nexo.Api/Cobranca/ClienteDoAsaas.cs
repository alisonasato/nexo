using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Nexo.Api.Cobranca;

/// <summary>
/// A conversa com o Asaas, reduzida ao que esta aplicação precisa.
///
/// <para>
/// São duas chamadas: garantir que o cliente existe lá, e criar a cobrança.
/// Tudo o mais que o PSP oferece — assinaturas, parcelamento, cartão, split,
/// negativação — fica de fora até existir alguém pedindo.
/// </para>
/// <para>
/// <b>A forma de pagamento é <c>UNDEFINED</c>.</b> Não é omissão: é o Asaas
/// gerando boleto e Pix na mesma cobrança e deixando o cliente escolher na
/// hora de pagar. Fixar boleto obrigaria o escritório a adivinhar, por cliente,
/// como cada um prefere pagar — e a errar, cobrando tarifa de boleto de quem
/// ia pagar por Pix.
/// </para>
/// </summary>
public sealed class ClienteDoAsaas
{
    private readonly HttpClient _http;
    private readonly OpcoesDoAsaas _opcoes;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ClienteDoAsaas(HttpClient http, IOptions<OpcoesDoAsaas> opcoes)
    {
        _opcoes = opcoes.Value;
        _http = http;

        _http.BaseAddress = new Uri(
            _opcoes.Endereco.EndsWith('/') ? _opcoes.Endereco : _opcoes.Endereco + "/");

        _http.DefaultRequestHeaders.Add("access_token", _opcoes.Chave);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(_opcoes.Aplicacao, "1.0"));
    }

    /// <summary>
    /// O identificador do pagador no Asaas, criando-o se ainda não existir lá.
    ///
    /// <para>
    /// O documento é obrigatório do lado deles, e não é capricho: boleto e Pix
    /// levam o CPF ou CNPJ do pagador. Quem chamar isto já precisa ter
    /// conferido — aqui a falta vira erro do PSP, com mensagem de PSP.
    /// </para>
    /// </summary>
    public async Task<string> GarantirCliente(
        string nome,
        string documento,
        string email,
        string referencia,
        CancellationToken cancelamento)
    {
        var resposta = await _http.PostAsJsonAsync(
            "customers",
            new ClienteNovo(nome, documento, string.IsNullOrWhiteSpace(email) ? null : email, referencia),
            Json,
            cancelamento);

        var criado = await Ler<ClienteDoPsp>(resposta, cancelamento);
        return criado.Id;
    }

    /// <param name="referencia">
    /// O que amarra a cobrança de volta ao recebível daqui. Volta intacto em
    /// toda notificação, e é por ele que o webhook descobre o que baixar.
    /// </param>
    public async Task<CobrancaCriada> CriarCobranca(
        string cliente,
        decimal valor,
        DateOnly vencimento,
        string descricao,
        string referencia,
        CancellationToken cancelamento)
    {
        var resposta = await _http.PostAsJsonAsync(
            "payments",
            new CobrancaNova(
                cliente,
                "UNDEFINED",
                valor,
                vencimento.ToString("yyyy-MM-dd"),
                descricao,
                referencia),
            Json,
            cancelamento);

        return await Ler<CobrancaCriada>(resposta, cancelamento);
    }

    /// <summary>
    /// Lê a resposta, transformando erro do PSP em exceção com o motivo dele.
    ///
    /// O Asaas devolve <c>{"errors":[{"code","description"}]}</c> com 400. A
    /// descrição é escrita para quem integra, e repassá-la é melhor do que
    /// trocá-la por um "não foi possível" genérico: "CPF ou CNPJ inválido" diz
    /// o que consertar, e some se for engolido.
    /// </summary>
    private static async Task<T> Ler<T>(HttpResponseMessage resposta, CancellationToken cancelamento)
    {
        if (resposta.IsSuccessStatusCode)
        {
            var corpo = await resposta.Content.ReadFromJsonAsync<T>(Json, cancelamento);
            return corpo ?? throw new FalhaNoAsaas("O PSP respondeu sem corpo.");
        }

        var erros = await Descrever(resposta, cancelamento);
        throw new FalhaNoAsaas(erros);
    }

    private static async Task<string> Descrever(
        HttpResponseMessage resposta,
        CancellationToken cancelamento)
    {
        try
        {
            var falha = await resposta.Content.ReadFromJsonAsync<RespostaComErros>(Json, cancelamento);

            if (falha?.Errors is { Count: > 0 } lista)
                return string.Join(" ", lista.Select(erro => erro.Description));
        }
        catch (JsonException)
        {
            /* Resposta que não é JSON — página de manutenção, proxy no meio.
               O código HTTP ainda diz algo, e é melhor que engolir. */
        }

        return $"O PSP respondeu {(int)resposta.StatusCode}.";
    }

    private record ClienteNovo(
        string Name,
        string CpfCnpj,
        string? Email,
        string ExternalReference);

    private record ClienteDoPsp(string Id);

    private record CobrancaNova(
        string Customer,
        string BillingType,
        decimal Value,
        string DueDate,
        string Description,
        string ExternalReference);

    private record RespostaComErros(List<ErroDoPsp>? Errors);

    private record ErroDoPsp(string? Code, string? Description);
}

/// <param name="InvoiceUrl">
/// A página onde o cliente escolhe entre boleto e Pix e paga. É o que o
/// escritório manda para ele — não o boleto direto, que fecharia a escolha.
/// </param>
public record CobrancaCriada(string Id, string InvoiceUrl, string? BankSlipUrl);

/// <summary>O PSP recusou, e o motivo dele é a melhor coisa a mostrar.</summary>
public sealed class FalhaNoAsaas(string motivo) : Exception(motivo);
