using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// Desconto, juros e multa na baixa.
///
/// <para>
/// Quando informados, eles explicam por que o valor pago difere do valor, e por
/// isso precisam fechar com ele. O que se confere é o que deixaria a baixa com
/// um número sem explicação: acréscimos que não fecham, valor negativo, a baixa
/// sem acréscimos perdendo a liberdade que sempre teve, e o estorno deixando
/// para trás os acréscimos da baixa que desfez.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class AcrescimosNaBaixa(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Juros_e_multa_que_fecham_ficam_na_baixa_e_a_conta_recebe_o_valor_pago()
    {
        var http = await Entrar();
        var conta = await ContasBancariasDeTeste.Criar(http);
        var lancamento = await Lancar(http, 1_000m);

        var resposta = await Baixar(http, lancamento, conta, 1_050m, juros: 30m, multa: 20m);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        var baixado = (await resposta.Content.ReadFromJsonAsync<LancamentoNaLista>(Json))!;
        Assert.Equal(1_050m, baixado.ValorPago);
        Assert.Equal(30m, baixado.Juros);
        Assert.Equal(20m, baixado.Multa);
        Assert.Null(baixado.Desconto);

        var listado = Assert.Single((await Listar(http)).Itens);
        Assert.Equal(30m, listado.Juros);
        Assert.Equal(20m, listado.Multa);

        /* Na conta entra o que entrou de fato, acréscimos incluídos. */
        var contas = (await http.GetFromJsonAsync<List<ContaNaLista>>("/contas-bancarias", Json))!;
        Assert.Equal(1_050m, contas.Single(item => item.Id == conta).SaldoAtual);
    }

    [Fact]
    public async Task Acrescimos_que_nao_fecham_e_valor_negativo_sao_recusados()
    {
        var http = await Entrar();
        var conta = await ContasBancariasDeTeste.Criar(http);
        var lancamento = await Lancar(http, 1_000m);

        /* 1.000 com 20 de juros dá 1.020: pagos 1.050 deixariam 30 sem explicação. */
        Assert.Equal("valorPago", await CampoRecusado(await Baixar(http, lancamento, conta, 1_050m, juros: 20m)));
        Assert.Equal("desconto", await CampoRecusado(await Baixar(http, lancamento, conta, 1_000m, desconto: -5m)));

        Assert.Equal(SituacaoLancamento.Aberto, Assert.Single((await Listar(http)).Itens).Situacao);
    }

    [Fact]
    public async Task Sem_acrescimos_o_valor_pago_continua_livre_e_o_estorno_limpa_os_acrescimos()
    {
        var http = await Entrar();
        var conta = await ContasBancariasDeTeste.Criar(http);
        var lancamento = await Lancar(http, 1_000m);

        /* Zero é o mesmo que não informar: a baixa de sempre, com o valor que entrou. */
        (await Baixar(http, lancamento, conta, 900m, desconto: 0m)).EnsureSuccessStatusCode();
        Assert.Null(Assert.Single((await Listar(http)).Itens).Desconto);
        (await http.PostAsync($"/lancamentos/{lancamento}/estornar", null)).EnsureSuccessStatusCode();

        (await Baixar(http, lancamento, conta, 900m, desconto: 100m)).EnsureSuccessStatusCode();
        Assert.Equal(100m, Assert.Single((await Listar(http)).Itens).Desconto);

        /* A baixa desfeita leva junto o que a explicava: o lançamento reaberto não tem desconto nenhum. */
        (await http.PostAsync($"/lancamentos/{lancamento}/estornar", null)).EnsureSuccessStatusCode();
        var reaberto = Assert.Single((await Listar(http)).Itens);
        Assert.Equal(SituacaoLancamento.Aberto, reaberto.Situacao);
        Assert.Null(reaberto.Desconto);
    }

    /* --------------------------------------------------------- apoio */

    private async Task<HttpClient> Entrar() =>
        await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

    /// <summary>Um lançamento a receber, de um cliente novo, na competência de março.</summary>
    private static async Task<Guid> Lancar(HttpClient http, decimal valor)
    {
        var pessoa = await http.PostAsJsonAsync("/pessoas", new DadosDePessoa(
            TipoPessoa.Juridica, RegimeTributario.SimplesNacional, string.Empty, [Papel.Cliente],
            "Cliente do teste", string.Empty, Documentos.CnpjValido(), string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, null, string.Empty, true), Json);
        pessoa.EnsureSuccessStatusCode();
        var pessoaId = (await pessoa.Content.ReadFromJsonAsync<PessoaDetalhada>(Json))!.Id;

        var lancamento = await http.PostAsJsonAsync("/lancamentos", new DadosDoAvulso(
            NaturezaLancamento.Receber, pessoaId, "Honorários", valor, new DateOnly(2026, 3, 10), 2026, 3), Json);
        lancamento.EnsureSuccessStatusCode();
        return (await lancamento.Content.ReadFromJsonAsync<LancamentoNaLista>(Json))!.Id;
    }

    private static Task<HttpResponseMessage> Baixar(
        HttpClient http, Guid lancamento, Guid conta, decimal valorPago,
        decimal? desconto = null, decimal? juros = null, decimal? multa = null) =>
        http.PostAsJsonAsync($"/lancamentos/{lancamento}/baixar",
            new DadosDaBaixa(conta, valorPago, new DateOnly(2026, 3, 10), desconto, juros, multa), Json);

    private static async Task<PaginaDeLancamentos> Listar(HttpClient http) =>
        (await http.GetFromJsonAsync<PaginaDeLancamentos>("/lancamentos?natureza=Receber&ano=2026&mes=3", Json))!;

    private static async Task<string> CampoRecusado(HttpResponseMessage resposta)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>(Json);
        return Assert.Single(corpo!.Problemas).Campo;
    }
}
