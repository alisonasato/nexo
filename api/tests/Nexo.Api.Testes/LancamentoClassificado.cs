using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// O lançamento com categoria e centro de custo.
///
/// <para>
/// O que se confere é o que faria o relatório de despesas mentir sem erro
/// nenhum: despesa classificada numa categoria de receita, categoria inativa
/// recebendo lançamento novo, parcela de acordo nascendo sem a classificação do
/// título que substituiu, e lançamento pago que não se deixa classificar.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class LancamentoClassificado(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Lancar_com_categoria_e_centro_devolve_e_lista_a_classificacao()
    {
        var http = await Entrar();
        var ocupacao = await CriarCategoria(http, "Ocupação", NaturezaLancamento.Pagar);
        var matriz = await CriarCentro(http, "Matriz");

        var resposta = await Lancar(http, NaturezaLancamento.Pagar, 1_500m, ocupacao, matriz);
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);

        var criado = (await resposta.Content.ReadFromJsonAsync<LancamentoNaLista>(Json))!;
        Assert.Equal(("Ocupação", "Matriz"), (criado.Categoria, criado.CentroDeCusto));

        var listado = Assert.Single((await Listar(http, NaturezaLancamento.Pagar)).Itens);
        Assert.Equal((ocupacao, matriz), (listado.CategoriaId, listado.CentroDeCustoId));
        Assert.Equal("Ocupação", listado.Categoria);
    }

    [Fact]
    public async Task Categoria_de_outra_natureza_ou_inativa_e_centro_inativo_sao_recusados()
    {
        var http = await Entrar();
        var ocupacao = await CriarCategoria(http, "Ocupação", NaturezaLancamento.Pagar);

        /* Uma receita classificada como despesa faria o total de despesas somar receita. */
        var trocada = await Lancar(http, NaturezaLancamento.Receber, 300m, ocupacao, null);
        Assert.Equal("categoriaId", await CampoRecusado(trocada));

        var antiga = await CriarCategoria(http, "Categoria antiga", NaturezaLancamento.Pagar);
        (await http.PutAsJsonAsync($"/categorias/{antiga}",
            new DadosDaCategoria("Categoria antiga", NaturezaLancamento.Pagar, null, false), Json)).EnsureSuccessStatusCode();
        Assert.Equal("categoriaId", await CampoRecusado(await Lancar(http, NaturezaLancamento.Pagar, 300m, antiga, null)));

        var fechado = await CriarCentro(http, "Filial fechada");
        (await http.PutAsJsonAsync($"/centros-de-custo/{fechado}",
            new DadosDoCentro("Filial fechada", false), Json)).EnsureSuccessStatusCode();
        Assert.Equal("centroDeCustoId", await CampoRecusado(await Lancar(http, NaturezaLancamento.Pagar, 300m, null, fechado)));

        /* A categoria de outro escritório não é encontrada: a política de isolamento não a enxerga. */
        var outroEscritorio = await Entrar();
        var alheia = await CriarCategoria(outroEscritorio, "Ocupação", NaturezaLancamento.Pagar);
        Assert.Equal("categoriaId", await CampoRecusado(await Lancar(http, NaturezaLancamento.Pagar, 300m, alheia, null)));
    }

    [Fact]
    public async Task Parcelas_e_renegociacao_herdam_a_classificacao()
    {
        var http = await Entrar();
        var sistemas = await CriarCategoria(http, "Sistemas", NaturezaLancamento.Pagar);
        var matriz = await CriarCentro(http, "Matriz");
        var fornecedor = await CriarPessoa(http, Papel.Fornecedor);

        var parcelado = await http.PostAsJsonAsync("/lancamentos/parcelamentos",
            new DadosDoParcelamento(NaturezaLancamento.Pagar, fornecedor, "Licença anual", 1_200m, 3,
                new DateOnly(2026, 3, 10), 2026, 3, sistemas, matriz), Json);
        parcelado.EnsureSuccessStatusCode();

        var parcelas = (await parcelado.Content.ReadFromJsonAsync<ParcelamentoCriado>(Json))!.Parcelas;
        Assert.All(parcelas, parcela => Assert.Equal((sistemas, matriz), (parcela.CategoriaId, parcela.CentroDeCustoId)));

        var acordo = await http.PostAsJsonAsync($"/lancamentos/{parcelas[0].Id}/renegociar",
            new DadosDaRenegociacao(2, new DateOnly(2026, 4, 10), 0m, 0m, 0m, "Fornecedor aceitou dividir"), Json);
        acordo.EnsureSuccessStatusCode();

        var novas = (await acordo.Content.ReadFromJsonAsync<RenegociacaoCriada>(Json))!.Parcelas;
        Assert.All(novas, parcela => Assert.Equal((sistemas, matriz), (parcela.CategoriaId, parcela.CentroDeCustoId)));
    }

    [Fact]
    public async Task Classificar_vale_ate_para_lancamento_pago_e_vazio_tira_a_classificacao()
    {
        var http = await Entrar();
        var honorarios = await CriarCategoria(http, "Honorários", NaturezaLancamento.Receber);
        var conta = await ContasBancariasDeTeste.Criar(http);

        var criado = await Lancar(http, NaturezaLancamento.Receber, 800m, null, null);
        var lancamento = (await criado.Content.ReadFromJsonAsync<LancamentoNaLista>(Json))!.Id;

        (await http.PostAsJsonAsync($"/lancamentos/{lancamento}/baixar",
            new DadosDaBaixa(conta, 800m, new DateOnly(2026, 3, 10)), Json)).EnsureSuccessStatusCode();

        /* Classificar não mexe em dinheiro, e é no pago que o relatório do mês procura. */
        var classificado = await http.PutAsJsonAsync($"/lancamentos/{lancamento}/classificacao",
            new DadosDaClassificacao(honorarios, null), Json);
        Assert.Equal(HttpStatusCode.OK, classificado.StatusCode);

        var lido = (await classificado.Content.ReadFromJsonAsync<LancamentoNaLista>(Json))!;
        Assert.Equal(("Honorários", SituacaoLancamento.Pago), (lido.Categoria, lido.Situacao));

        /* Inativar a categoria não trava reenviar a mesma classificação. */
        (await http.PutAsJsonAsync($"/categorias/{honorarios}",
            new DadosDaCategoria("Honorários", NaturezaLancamento.Receber, null, false), Json)).EnsureSuccessStatusCode();
        var mesma = await http.PutAsJsonAsync($"/lancamentos/{lancamento}/classificacao",
            new DadosDaClassificacao(honorarios, null), Json);
        Assert.Equal(HttpStatusCode.OK, mesma.StatusCode);

        var semNada = await http.PutAsJsonAsync($"/lancamentos/{lancamento}/classificacao",
            new DadosDaClassificacao(null, null), Json);
        Assert.Null((await semNada.Content.ReadFromJsonAsync<LancamentoNaLista>(Json))!.CategoriaId);
    }

    /* --------------------------------------------------------- apoio */

    private async Task<HttpClient> Entrar() =>
        await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

    private static async Task<Guid> CriarCategoria(HttpClient http, string nome, NaturezaLancamento natureza)
    {
        var resposta = await http.PostAsJsonAsync("/categorias", new DadosDaCategoria(nome, natureza, null), Json);
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<CategoriaNaLista>(Json))!.Id;
    }

    private static async Task<Guid> CriarCentro(HttpClient http, string nome)
    {
        var resposta = await http.PostAsJsonAsync("/centros-de-custo", new DadosDoCentro(nome), Json);
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<CentroNaLista>(Json))!.Id;
    }

    private static async Task<Guid> CriarPessoa(HttpClient http, Papel papel)
    {
        var resposta = await http.PostAsJsonAsync("/pessoas", new DadosDePessoa(
            TipoPessoa.Juridica, RegimeTributario.SimplesNacional, string.Empty, [papel],
            "Pessoa do teste", string.Empty, Documentos.CnpjValido(), string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, null, string.Empty, true), Json);

        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<PessoaDetalhada>(Json))!.Id;
    }

    private static async Task<HttpResponseMessage> Lancar(
        HttpClient http, NaturezaLancamento natureza, decimal valor, Guid? categoria, Guid? centro)
    {
        var pessoa = await CriarPessoa(http, natureza == NaturezaLancamento.Pagar ? Papel.Fornecedor : Papel.Cliente);

        return await http.PostAsJsonAsync("/lancamentos", new DadosDoAvulso(
            natureza, pessoa, "Lançamento do teste", valor, new DateOnly(2026, 3, 10), 2026, 3, categoria, centro), Json);
    }

    private static async Task<PaginaDeLancamentos> Listar(HttpClient http, NaturezaLancamento natureza) =>
        (await http.GetFromJsonAsync<PaginaDeLancamentos>($"/lancamentos?natureza={natureza}", Json))!;

    private static async Task<string> CampoRecusado(HttpResponseMessage resposta)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>(Json);
        return Assert.Single(corpo!.Problemas).Campo;
    }
}
