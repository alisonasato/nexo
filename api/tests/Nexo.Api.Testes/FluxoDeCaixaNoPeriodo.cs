using System.Net.Http.Json;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;
using static Nexo.Api.Testes.Apoio;

namespace Nexo.Api.Testes;

/// <summary>
/// O fluxo de caixa pela borda HTTP.
///
/// <para>
/// As datas são contadas a partir de hoje, e não fixas: é o dia de hoje que
/// separa o realizado do projetado, e um teste com datas fixas mudaria de
/// resultado conforme o dia em que rodasse.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class FluxoDeCaixaNoPeriodo(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    private static readonly DateOnly Hoje = DateOnly.FromDateTime(DateTime.Today);

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Saldo_no_inicio_leva_o_saldo_inicial_e_o_que_se_movimentou_antes()
    {
        var http = await Entrar();
        var conta = await ContasBancariasDeTeste.Criar(http, saldo: 1_000m);

        await LancarEBaixar(http, conta, NaturezaLancamento.Receber, 300m, Hoje.AddDays(-10));
        await LancarEBaixar(http, conta, NaturezaLancamento.Pagar, 100m, Hoje.AddDays(-5));

        var fluxo = await Fluxo(http, Hoje.AddDays(-7), Hoje);

        /* A entrada de dez dias atrás está antes do período, e já dentro do saldo do começo dele. */
        Assert.Equal(1_300m, fluxo.SaldoNoInicio);
        Assert.Equal(8, fluxo.Periodos.Count);
        Assert.Equal(100m, Assert.Single(fluxo.Periodos, periodo => periodo.Comeco == Hoje.AddDays(-5)).Saidas);
        Assert.Equal(1_200m, fluxo.SaldoNoFim);
    }

    [Fact]
    public async Task Projecao_comeca_hoje_e_o_atraso_fica_de_fora()
    {
        var http = await Entrar();
        await ContasBancariasDeTeste.Criar(http, saldo: 1_000m);

        await Lancar(http, NaturezaLancamento.Receber, 500m, Hoje.AddDays(3));
        await Lancar(http, NaturezaLancamento.Pagar, 200m, Hoje.AddDays(5));
        await Lancar(http, NaturezaLancamento.Receber, 80m, Hoje.AddDays(-2));

        var cancelado = await Lancar(http, NaturezaLancamento.Pagar, 999m, Hoje.AddDays(4));
        (await http.PostAsJsonAsync($"/lancamentos/{cancelado}/cancelar",
            new DadosDoCancelamento("Lançado por engano"), Json)).EnsureSuccessStatusCode();

        var fluxo = await Fluxo(http, Hoje, Hoje.AddDays(10));

        Assert.Equal(500m, fluxo.Periodos.Single(periodo => periodo.Comeco == Hoje.AddDays(3)).AReceber);
        Assert.Equal(200m, fluxo.Periodos.Single(periodo => periodo.Comeco == Hoje.AddDays(5)).APagar);

        /* Cancelado não é dinheiro que vai sair. */
        Assert.Equal(0m, fluxo.Periodos.Single(periodo => periodo.Comeco == Hoje.AddDays(4)).APagar);

        /*
         * O vencido de dois dias atrás aparece à parte e não entra no saldo: não
         * se sabe quando, nem se, ele vai entrar.
         */
        Assert.Equal(80m, fluxo.EmAtrasoAReceber);
        Assert.Equal(1_300m, fluxo.SaldoNoFim);
    }

    [Fact]
    public async Task Por_mes_cada_mes_soma_os_seus_dias()
    {
        var http = await Entrar();
        var proximo = new DateOnly(Hoje.Year, Hoje.Month, 1).AddMonths(1);
        var seguinte = proximo.AddMonths(1);

        await Lancar(http, NaturezaLancamento.Receber, 100m, proximo.AddDays(4));
        await Lancar(http, NaturezaLancamento.Receber, 250m, proximo.AddDays(19));
        await Lancar(http, NaturezaLancamento.Pagar, 70m, seguinte.AddDays(4));

        var fluxo = await Fluxo(http, proximo, seguinte.AddMonths(1).AddDays(-1), AgrupamentoDoFluxo.Mes);

        Assert.Equal(2, fluxo.Periodos.Count);
        Assert.Equal((proximo, 350m), (fluxo.Periodos[0].Comeco, fluxo.Periodos[0].AReceber));
        Assert.Equal((seguinte, 70m), (fluxo.Periodos[1].Comeco, fluxo.Periodos[1].APagar));

        /* Sem conta nenhuma, o saldo parte de zero. */
        Assert.Equal(280m, fluxo.SaldoNoFim);
    }

    [Fact]
    public async Task Periodo_invertido_ou_maior_que_um_ano_e_recusado()
    {
        var http = await Entrar();

        var invertido = await http.GetAsync($"/fluxo-de-caixa?de={Hoje:yyyy-MM-dd}&ate={Hoje.AddDays(-1):yyyy-MM-dd}");
        Assert.Equal("ate", await CampoRecusado(invertido));

        var longo = await http.GetAsync($"/fluxo-de-caixa?de={Hoje:yyyy-MM-dd}&ate={Hoje.AddDays(366):yyyy-MM-dd}");
        Assert.Equal("ate", await CampoRecusado(longo));
    }

    /* --------------------------------------------------------- apoio */

    private async Task<HttpClient> Entrar() =>
        await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

    private static async Task<FluxoNoPeriodo> Fluxo(
        HttpClient http, DateOnly de, DateOnly ate, AgrupamentoDoFluxo agrupamento = AgrupamentoDoFluxo.Dia) =>
        (await http.GetFromJsonAsync<FluxoNoPeriodo>(
            $"/fluxo-de-caixa?de={de:yyyy-MM-dd}&ate={ate:yyyy-MM-dd}&agrupamento={agrupamento}", Json))!;

    /// <summary>Um lançamento avulso que vence na data, com uma pessoa que tem o papel que a natureza pede.</summary>
    private static async Task<Guid> Lancar(HttpClient http, NaturezaLancamento natureza, decimal valor, DateOnly vencimento)
    {
        var papel = natureza == NaturezaLancamento.Pagar ? Papel.Fornecedor : Papel.Cliente;

        var pessoa = await http.PostAsJsonAsync("/pessoas", new DadosDePessoa(
            TipoPessoa.Juridica, RegimeTributario.SimplesNacional, string.Empty, [papel],
            "Pessoa do teste", string.Empty, Documentos.CnpjValido(), string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, null, string.Empty, true), Json);
        pessoa.EnsureSuccessStatusCode();
        var pessoaId = (await pessoa.Content.ReadFromJsonAsync<PessoaDetalhada>(Json))!.Id;

        var lancamento = await http.PostAsJsonAsync("/lancamentos", new DadosDoAvulso(
            natureza, pessoaId, "Lançamento do teste", valor, vencimento, vencimento.Year, vencimento.Month), Json);
        lancamento.EnsureSuccessStatusCode();

        return (await lancamento.Content.ReadFromJsonAsync<LancamentoNaLista>(Json))!.Id;
    }

    private static async Task LancarEBaixar(
        HttpClient http, Guid conta, NaturezaLancamento natureza, decimal valor, DateOnly dia)
    {
        var lancamento = await Lancar(http, natureza, valor, dia);

        var baixa = await http.PostAsJsonAsync($"/lancamentos/{lancamento}/baixar",
            new DadosDaBaixa(conta, valor, dia), Json);
        baixa.EnsureSuccessStatusCode();
    }
}
