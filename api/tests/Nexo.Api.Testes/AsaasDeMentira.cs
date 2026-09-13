using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Nexo.Api.Testes;

/// <summary>
/// O PSP, sem rede.
///
/// <para>
/// Os testes não podem sair para a internet: o CI ficaria refém de serviço de
/// terceiro, e um teste que falha quando o Asaas cai não testa o Nexo. É o
/// mesmo motivo pelo qual a consulta de CEP já é trocada nos testes.
/// </para>
/// <para>
/// Ele <b>conta</b> as chamadas, e é isso que torna verificável o que mais
/// importa aqui: que cobrar duas vezes não cria duas cobranças, que um cadastro
/// sem documento é recusado <b>antes</b> de a requisição sair, e que cancelar
/// um lançamento cobrado tira a cobrança do ar.
/// </para>
/// </summary>
public sealed class AsaasDeMentira : HttpMessageHandler
{
    public int ClientesCriados { get; private set; }
    public int CobrancasCriadas { get; private set; }

    /// <summary>As cobranças tiradas do ar, na ordem em que foram.</summary>
    public List<string> CobrancasExcluidas { get; } = [];

    /// <summary>Quando preenchido, responde a qualquer chamada como o Asaas responde a erro.</summary>
    public string? Recusa { get; set; }

    /// <summary>
    /// Quando preenchido, recusa só a exclusão, como o PSP recusa tirar do ar
    /// uma cobrança que o cliente acabou de pagar.
    /// </summary>
    public string? RecusaAoExcluir { get; set; }

    public JsonElement UltimaCobranca { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage pedido,
        CancellationToken cancelamento)
    {
        var caminho = pedido.RequestUri!.AbsolutePath;

        if (Recusa is { } motivo) return Erro(motivo);

        if (pedido.Method == HttpMethod.Delete && caminho.Contains("/payments/"))
        {
            if (RecusaAoExcluir is { } motivoDaExclusao) return Erro(motivoDaExclusao);

            var id = caminho[(caminho.LastIndexOf('/') + 1)..];
            CobrancasExcluidas.Add(id);
            return Ok(new { deleted = true, id });
        }

        if (caminho.EndsWith("/customers"))
        {
            ClientesCriados++;
            return Ok(new { id = "cus_" + ClientesCriados.ToString("000000") });
        }

        if (caminho.EndsWith("/payments"))
        {
            CobrancasCriadas++;

            /* O corpo é lido para o teste poder conferir o que foi mandado. */
            UltimaCobranca = await pedido.Content!.ReadFromJsonAsync<JsonElement>(cancelamento);

            var id = "pay_" + CobrancasCriadas.ToString("000000");
            return Ok(new
            {
                id,
                invoiceUrl = "https://sandbox.asaas.com/i/" + id,
                bankSlipUrl = (string?)null,
            });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Ok(object corpo) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(corpo) };

    private static HttpResponseMessage Erro(string motivo) =>
        new(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new
            {
                errors = new[] { new { code = "invalid_action", description = motivo } },
            }),
        };
}
