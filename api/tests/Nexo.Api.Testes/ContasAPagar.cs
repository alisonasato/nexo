using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Api.Cobranca;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// Lançamentos a pagar, na mesma tabela e pelas mesmas rotas dos a receber.
///
/// <para>
/// O risco de juntar as duas naturezas numa tabela é uma vazar na outra: a
/// conta do aluguel entrando no total a receber, um fornecedor recebendo
/// boleto, a parcela renegociada nascendo do lado errado. Os testes conferem
/// cada uma dessas fronteiras.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class ContasAPagar : IDisposable
{
    private readonly BancoDeTestes _banco;
    private readonly AsaasDeMentira _psp = new();
    private readonly AplicacaoDeTestes _aplicacao;

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public ContasAPagar(BancoDeTestes banco)
    {
        _banco = banco;

        /* Com a cobrança ligada: a recusa de cobrar tem de vir da natureza, e não da falta de chave. */
        _aplicacao = new AplicacaoDeTestes(banco.Conexao, servicos =>
        {
            servicos.Configure<OpcoesDoAsaas>(opcoes => opcoes.Chave = "chave-de-mentira");

            servicos.AddHttpClient<ClienteDoAsaas>()
                .ConfigurePrimaryHttpMessageHandler(() => _psp);
        });
    }

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task A_pagar_pede_fornecedor_e_a_receber_pede_cliente()
    {
        var http = await Entrar();
        var cliente = await CriarPessoa(http, "Cliente do teste", Papel.Cliente);
        var fornecedor = await CriarPessoa(http, "Imobiliária do teste", Papel.Fornecedor);

        var pagarAoCliente = await Lancar(http, NaturezaLancamento.Pagar, cliente, 100m);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, pagarAoCliente.StatusCode);
        Assert.Equal("pessoaId", (await ProblemaUnico(pagarAoCliente)).Campo);

        var cobrarDoFornecedor = await Lancar(http, NaturezaLancamento.Receber, fornecedor, 100m);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, cobrarDoFornecedor.StatusCode);
        Assert.Equal("pessoaId", (await ProblemaUnico(cobrarDoFornecedor)).Campo);

        var aluguel = await Lancar(http, NaturezaLancamento.Pagar, fornecedor, 100m);
        Assert.Equal(HttpStatusCode.Created, aluguel.StatusCode);
        Assert.Equal(NaturezaLancamento.Pagar, (await Lido(aluguel)).Natureza);
    }

    [Fact]
    public async Task Lancamento_sem_natureza_e_recusado()
    {
        var http = await Entrar();
        var fornecedor = await CriarPessoa(http, "Imobiliária do teste", Papel.Fornecedor);

        /*
         * Sem padrão escondido: o corpo que não diz a natureza não vira a
         * receber por omissão. É esse esquecimento que inverteria o sentido do
         * dinheiro.
         */
        var resposta = await http.PostAsJsonAsync("/lancamentos", new
        {
            pessoaId = fornecedor,
            descricao = "Aluguel de março",
            valor = 1_500m,
            vencimento = "2026-03-10",
            competenciaAno = 2026,
            competenciaMes = 3,
        }, Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Contains("natureza", (await ProblemasDa(resposta)).Select(problema => problema.Campo));
    }

    [Fact]
    public async Task Lista_e_totais_ficam_cada_um_na_sua_natureza()
    {
        var http = await Entrar();
        var cliente = await CriarPessoa(http, "Cliente do teste", Papel.Cliente);
        var fornecedor = await CriarPessoa(http, "Imobiliária do teste", Papel.Fornecedor);

        (await Lancar(http, NaturezaLancamento.Receber, cliente, 800m)).EnsureSuccessStatusCode();
        (await Lancar(http, NaturezaLancamento.Pagar, fornecedor, 300m)).EnsureSuccessStatusCode();

        var aReceber = await Listar(http, "?natureza=Receber&ano=2026&mes=3");
        Assert.Equal(800m, Assert.Single(aReceber.Itens).Valor);
        Assert.Equal(800m, aReceber.TotalEmAberto);

        var aPagar = await Listar(http, "?natureza=Pagar&ano=2026&mes=3");
        Assert.Equal(300m, Assert.Single(aPagar.Itens).Valor);
        Assert.Equal(300m, aPagar.TotalEmAberto);

        /* Sem natureza é a receber: é o que a listagem sempre foi. */
        Assert.Equal(800m, (await Listar(http, "?ano=2026&mes=3")).TotalEmAberto);
    }

    [Fact]
    public async Task Baixa_a_pagar_soma_no_total_pago_so_da_natureza_dela()
    {
        var http = await Entrar();
        var cliente = await CriarPessoa(http, "Cliente do teste", Papel.Cliente);
        var fornecedor = await CriarPessoa(http, "Imobiliária do teste", Papel.Fornecedor);

        (await Lancar(http, NaturezaLancamento.Receber, cliente, 800m)).EnsureSuccessStatusCode();
        var conta = await Lido(await Lancar(http, NaturezaLancamento.Pagar, fornecedor, 300m));

        var lote = await http.PostAsJsonAsync("/lancamentos/baixar-em-lote",
            new DadosDaBaixaEmLote(await ContasBancariasDeTeste.Criar(http), [conta.Id], new DateOnly(2026, 3, 10)), Json);
        Assert.Equal(1, (await lote.Content.ReadFromJsonAsync<ResultadoDaBaixaEmLote>(Json))!.Baixados);

        Assert.Equal(300m, (await Listar(http, "?natureza=Pagar&ano=2026&mes=3")).TotalPago);
        Assert.Equal(0m, (await Listar(http, "?natureza=Receber&ano=2026&mes=3")).TotalPago);
    }

    [Fact]
    public async Task Lancamento_a_pagar_nao_se_cobra()
    {
        var http = await Entrar();
        var fornecedor = await CriarPessoa(http, "Imobiliária do teste", Papel.Fornecedor);
        var conta = await Lido(await Lancar(http, NaturezaLancamento.Pagar, fornecedor, 300m));

        var resposta = await http.PostAsync($"/lancamentos/{conta.Id}/cobrar", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal("natureza", (await ProblemaUnico(resposta)).Campo);
    }

    [Fact]
    public async Task Parcelas_e_renegociacao_de_conta_a_pagar_continuam_a_pagar()
    {
        var http = await Entrar();
        var fornecedor = await CriarPessoa(http, "Software do teste", Papel.Fornecedor);

        var parcelado = await http.PostAsJsonAsync("/lancamentos/parcelamentos",
            new DadosDoParcelamento(NaturezaLancamento.Pagar, fornecedor, "Licença anual", 1_200m, 3,
                new DateOnly(2026, 3, 10), 2026, 3), Json);
        parcelado.EnsureSuccessStatusCode();

        var parcelas = (await parcelado.Content.ReadFromJsonAsync<ParcelamentoCriado>(Json))!.Parcelas;
        Assert.All(parcelas, parcela => Assert.Equal(NaturezaLancamento.Pagar, parcela.Natureza));

        var acordo = await http.PostAsJsonAsync($"/lancamentos/{parcelas[0].Id}/renegociar",
            new DadosDaRenegociacao(2, new DateOnly(2026, 4, 10), 0m, 0m, 0m, "Fornecedor aceitou dividir"), Json);
        acordo.EnsureSuccessStatusCode();

        var novas = (await acordo.Content.ReadFromJsonAsync<RenegociacaoCriada>(Json))!.Parcelas;
        Assert.All(novas, parcela => Assert.Equal(NaturezaLancamento.Pagar, parcela.Natureza));

        Assert.Empty((await Listar(http, "?natureza=Receber&ano=2026&mes=3")).Itens);
    }

    /* --------------------------------------------------------- apoio */

    private async Task<HttpClient> Entrar()
    {
        var conta = await Contas.Criar(_banco, _aplicacao);
        return await Contas.Entrar(_aplicacao, conta);
    }

    private static async Task<Guid> CriarPessoa(HttpClient http, string nome, Papel papel)
    {
        var resposta = await http.PostAsJsonAsync("/pessoas", new DadosDePessoa(
            TipoPessoa.Juridica, RegimeTributario.SimplesNacional, string.Empty, [papel],
            nome, string.Empty, Documentos.CnpjValido(), string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, null, string.Empty, true), Json);

        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<PessoaDetalhada>(Json))!.Id;
    }

    private static Task<HttpResponseMessage> Lancar(
        HttpClient http, NaturezaLancamento natureza, Guid pessoa, decimal valor) =>
        http.PostAsJsonAsync("/lancamentos",
            new DadosDoAvulso(natureza, pessoa, "Lançamento do teste", valor, new DateOnly(2026, 3, 10), 2026, 3),
            Json);

    private static async Task<LancamentoNaLista> Lido(HttpResponseMessage resposta)
    {
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<LancamentoNaLista>(Json))!;
    }

    private static async Task<PaginaDeLancamentos> Listar(HttpClient http, string consulta) =>
        (await http.GetFromJsonAsync<PaginaDeLancamentos>($"/lancamentos{consulta}", Json))!;

    private static async Task<List<Problema>> ProblemasDa(HttpResponseMessage resposta) =>
        (await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>(Json))!.Problemas;

    private static async Task<Problema> ProblemaUnico(HttpResponseMessage resposta) =>
        Assert.Single(await ProblemasDa(resposta));
}
