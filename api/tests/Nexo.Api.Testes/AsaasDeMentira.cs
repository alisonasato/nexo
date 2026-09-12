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
/// importa aqui: que cobrar duas vezes não cria duas cobranças, e que um
/// cadastro sem documento é recusado <b>antes</b> de a requisição sair.
/// </para>
/// </summary>
public sealed class AsaasDeMentira : HttpMessageHandler
{
    public int ClientesCriados { get; private set; }
    public int CobrancasCriadas { get; private set; }

    /// <summary>Quando verdadeiro, responde como o Asaas responde a erro.</summary>
    public string? Recusa { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage pedido,
        CancellationToken cancelamento)
    {
        var caminho = pedido.RequestUri!.AbsolutePath;

        if (Recusa is { } motivo)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new
                {
                    errors = new[] { new { code = "invalid_action", description = motivo } },
                }),
            };
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

    public JsonElement UltimaCobranca { get; private set; }

    private static HttpResponseMessage Ok(object corpo) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(corpo) };
}
