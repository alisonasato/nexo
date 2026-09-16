using System.Net.Http.Json;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;
using static Nexo.Api.Testes.Apoio;

namespace Nexo.Api.Testes;

/// <summary>
/// Uma conta bancária para os testes que baixam.
///
/// Desde que a baixa passou a exigir a conta, todo teste que baixa precisa de
/// uma. O saldo começa no primeiro dia de 2026, antes de todas as datas de baixa
/// dos testes, e o nome é sorteado porque um mesmo teste pode criar várias.
/// </summary>
internal static class ContasBancariasDeTeste
{
    public static async Task<Guid> Criar(HttpClient http, bool recebeCobrancas = false, decimal saldo = 0m)
    {
        var resposta = await http.PostAsJsonAsync("/contas-bancarias", new DadosDaConta(
            $"Conta {Guid.NewGuid():N}"[..20], TipoConta.Corrente, "Banco do teste", "0001", "12345-6",
            saldo, new DateOnly(2026, 1, 1), Ativa: true, RecebeCobrancas: recebeCobrancas), Json);

        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<ContaNaLista>(Json))!.Id;
    }
}
