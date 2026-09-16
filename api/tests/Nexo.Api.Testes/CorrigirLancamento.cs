using System.Net;
using System.Net.Http.Json;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;
using static Nexo.Api.Testes.Apoio;

namespace Nexo.Api.Testes;

/// <summary>
/// Corrigir um lançamento em aberto, em vez de cancelar e lançar de novo.
///
/// <para>
/// O que se confere é o que faria a correção virar buraco: corrigir depois da
/// baixa, quando o dinheiro já se moveu, e corrigir para um valor que não
/// existe. A mudança fica na trilha — é o histórico que faltava quando o
/// conserto era cancelar e relançar.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class CorrigirLancamento(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Corrige_o_que_foi_digitado_errado_e_deixa_a_mudanca_na_trilha()
    {
        var http = await Entrar();
        var lancamento = await Lancar(http, 100m);

        var resposta = await Corrigir(http, lancamento,
            new DadosDaEdicao("Honorários de abril", 1_000m, new DateOnly(2026, 4, 15), 2026, 4));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        var corrigido = (await resposta.Content.ReadFromJsonAsync<LancamentoNaLista>(Json))!;
        Assert.Equal(1_000m, corrigido.Valor);
        Assert.Equal("Honorários de abril", corrigido.Descricao);
        Assert.Equal(new DateOnly(2026, 4, 15), corrigido.Vencimento);
        Assert.Equal((2026, 4), (corrigido.CompetenciaAno, corrigido.CompetenciaMes));

        var historico = (await http.GetFromJsonAsync<List<EventoNoHistorico>>(
            $"/lancamentos/{lancamento}/historico", Json))!;

        Assert.Contains(historico, evento =>
            evento.Mudancas.Any(mudanca => mudanca.Campo == "valor" && mudanca.Depois == "1000.00"));
    }

    [Fact]
    public async Task Valor_zero_e_lancamento_ja_baixado_sao_recusados()
    {
        var http = await Entrar();
        var lancamento = await Lancar(http, 100m);

        Assert.Equal("valor", await CampoRecusado(await Corrigir(http, lancamento, Dados(0m))));

        var conta = await ContasBancariasDeTeste.Criar(http);
        (await http.PostAsJsonAsync($"/lancamentos/{lancamento}/baixar",
            new DadosDaBaixa(conta, 100m, new DateOnly(2026, 3, 10)), Json)).EnsureSuccessStatusCode();

        /* Depois da baixa o dinheiro já se moveu: o conserto é estornar. */
        Assert.Equal("id", await CampoRecusado(await Corrigir(http, lancamento, Dados(200m))));
    }

    /* --------------------------------------------------------- apoio */

    private async Task<HttpClient> Entrar() =>
        await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

    private static DadosDaEdicao Dados(decimal valor) =>
        new("Honorários", valor, new DateOnly(2026, 3, 10), 2026, 3);

    private static Task<HttpResponseMessage> Corrigir(HttpClient http, Guid lancamento, DadosDaEdicao dados) =>
        http.PutAsJsonAsync($"/lancamentos/{lancamento}", dados, Json);

    private static async Task<Guid> Lancar(HttpClient http, decimal valor)
    {
        var pessoa = await CriarPessoa(http, Papel.Cliente);

        var resposta = await http.PostAsJsonAsync("/lancamentos", new DadosDoAvulso(
            NaturezaLancamento.Receber, pessoa, "Honorários", valor, new DateOnly(2026, 3, 10), 2026, 3), Json);

        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<LancamentoNaLista>(Json))!.Id;
    }
}
