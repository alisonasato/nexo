using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// O fechamento por categoria, mês a mês.
///
/// <para>
/// O que se confere é o que faria o relatório mentir com cara de certo: o mês
/// errado entre competência e caixa, o título renegociado somado junto com as
/// parcelas que o substituíram, o dinheiro sem categoria sumindo da conta, a
/// árvore contando o mesmo valor uma vez por nível, e o recorte por centro de
/// custo.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class FechamentoPorCategoria(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Competencia_soma_o_mes_do_servico_e_caixa_soma_o_mes_da_baixa()
    {
        var http = await Entrar();
        var honorarios = await CriarCategoria(http, "Honorários", NaturezaLancamento.Receber, null);
        var conta = await ContasBancariasDeTeste.Criar(http);

        /* O serviço é de março; o dinheiro entrou em abril, e menos do que o cobrado. */
        var lancamento = await Lancar(http, NaturezaLancamento.Receber, 1_000m, 2026, 3, honorarios);
        (await http.PostAsJsonAsync($"/lancamentos/{lancamento}/baixar",
            new DadosDaBaixa(conta, 900m, new DateOnly(2026, 4, 10)), Json)).EnsureSuccessStatusCode();

        var porCompetencia = await Relatorio(http, "deAno=2026&deMes=3&ateAno=2026&ateMes=4");
        var receitas = Receitas(porCompetencia);

        Assert.Equal([1_000m, 0m], receitas.Totais);
        Assert.Equal("Honorários", Assert.Single(receitas.Linhas).Categoria);
        Assert.Equal([1_000m, 0m], porCompetencia.Resultado);

        var porCaixa = await Relatorio(http, "regime=Caixa&deAno=2026&deMes=3&ateAno=2026&ateMes=4");
        Assert.Equal([0m, 900m], Receitas(porCaixa).Totais);
        Assert.Equal(RegimeDoRelatorio.Caixa, porCaixa.Regime);
    }

    [Fact]
    public async Task Cancelado_e_renegociado_ficam_de_fora_e_o_sem_categoria_tem_linha_propria()
    {
        var http = await Entrar();
        await Lancar(http, NaturezaLancamento.Receber, 300m, 2026, 3, null);

        var cancelado = await Lancar(http, NaturezaLancamento.Receber, 500m, 2026, 3, null);
        (await http.PostAsJsonAsync($"/lancamentos/{cancelado}/cancelar",
            new DadosDoCancelamento("Cliente desistiu"), Json)).EnsureSuccessStatusCode();

        /* Renegociar troca um título de 1.000 por duas parcelas de 500: contar os três somaria o dobro. */
        var renegociado = await Lancar(http, NaturezaLancamento.Receber, 1_000m, 2026, 3, null);
        (await http.PostAsJsonAsync($"/lancamentos/{renegociado}/renegociar",
            new DadosDaRenegociacao(2, new DateOnly(2026, 4, 10), 0m, 0m, 0m, "Cliente pediu prazo"), Json))
            .EnsureSuccessStatusCode();

        var receitas = Receitas(await Relatorio(http, "deAno=2026&deMes=3&ateAno=2026&ateMes=4"));

        Assert.Equal(1_300m, receitas.Total);
        Assert.Equal("Sem categoria", Assert.Single(receitas.Linhas).Categoria);
    }

    [Fact]
    public async Task A_arvore_soma_e_o_centro_de_custo_recorta()
    {
        var http = await Entrar();
        var pessoal = await CriarCategoria(http, "Pessoal", NaturezaLancamento.Pagar, null);
        var salarios = await CriarCategoria(http, "Salários", NaturezaLancamento.Pagar, pessoal);
        var matriz = await CriarCentro(http, "Matriz");
        var filial = await CriarCentro(http, "Filial");

        await Lancar(http, NaturezaLancamento.Pagar, 1_000m, 2026, 3, salarios, matriz);
        await Lancar(http, NaturezaLancamento.Pagar, 200m, 2026, 3, pessoal, filial);

        var inteiro = await Relatorio(http, "deAno=2026&deMes=3&ateAno=2026&ateMes=3");
        var despesas = Despesas(inteiro);

        /* A de cima responde pelas de baixo, e o total do grupo não conta o mesmo dinheiro duas vezes. */
        Assert.Equal(1_200m, despesas.Linhas.Single(linha => linha.Categoria == "Pessoal").Total);
        Assert.Equal(1_000m, despesas.Linhas.Single(linha => linha.Categoria == "Salários").Total);
        Assert.Equal(1_200m, despesas.Total);
        Assert.Equal([-1_200m], inteiro.Resultado);

        var daMatriz = Despesas(await Relatorio(http,
            $"deAno=2026&deMes=3&ateAno=2026&ateMes=3&centroDeCustoId={matriz}"));

        Assert.Equal(1_000m, daMatriz.Total);
        Assert.Equal(1_000m, daMatriz.Linhas.Single(linha => linha.Categoria == "Pessoal").Total);
    }

    [Fact]
    public async Task Periodo_invertido_ou_longo_demais_e_recusado()
    {
        var http = await Entrar();

        Assert.Equal("ate", await CampoRecusado(
            await http.GetAsync("/relatorios/por-categoria?deAno=2026&deMes=6&ateAno=2026&ateMes=3")));

        Assert.Equal("ate", await CampoRecusado(
            await http.GetAsync("/relatorios/por-categoria?deAno=2024&deMes=1&ateAno=2026&ateMes=12")));

        Assert.Equal("mes", await CampoRecusado(
            await http.GetAsync("/relatorios/por-categoria?ateAno=2026&ateMes=13")));
    }

    /* --------------------------------------------------------- apoio */

    private async Task<HttpClient> Entrar() =>
        await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

    private static GrupoDoRelatorio Receitas(RelatorioDeCategorias relatorio) =>
        relatorio.Grupos.Single(grupo => grupo.Natureza == NaturezaLancamento.Receber);

    private static GrupoDoRelatorio Despesas(RelatorioDeCategorias relatorio) =>
        relatorio.Grupos.Single(grupo => grupo.Natureza == NaturezaLancamento.Pagar);

    private static async Task<RelatorioDeCategorias> Relatorio(HttpClient http, string consulta) =>
        (await http.GetFromJsonAsync<RelatorioDeCategorias>($"/relatorios/por-categoria?{consulta}", Json))!;

    private static async Task<Guid> Lancar(
        HttpClient http,
        NaturezaLancamento natureza,
        decimal valor,
        int ano,
        int mes,
        Guid? categoria,
        Guid? centro = null)
    {
        var papel = natureza == NaturezaLancamento.Pagar ? Papel.Fornecedor : Papel.Cliente;

        var pessoa = await http.PostAsJsonAsync("/pessoas", new DadosDePessoa(
            TipoPessoa.Juridica, RegimeTributario.SimplesNacional, string.Empty, [papel],
            "Pessoa do teste", string.Empty, Documentos.CnpjValido(), string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, null, string.Empty, true), Json);
        pessoa.EnsureSuccessStatusCode();
        var pessoaId = (await pessoa.Content.ReadFromJsonAsync<PessoaDetalhada>(Json))!.Id;

        var lancamento = await http.PostAsJsonAsync("/lancamentos", new DadosDoAvulso(
            natureza, pessoaId, "Lançamento do teste", valor, new DateOnly(ano, mes, 10), ano, mes,
            categoria, centro), Json);
        lancamento.EnsureSuccessStatusCode();

        return (await lancamento.Content.ReadFromJsonAsync<LancamentoNaLista>(Json))!.Id;
    }

    private static async Task<Guid> CriarCategoria(HttpClient http, string nome, NaturezaLancamento natureza, Guid? pai)
    {
        var resposta = await http.PostAsJsonAsync("/categorias", new DadosDaCategoria(nome, natureza, pai), Json);
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<CategoriaNaLista>(Json))!.Id;
    }

    private static async Task<Guid> CriarCentro(HttpClient http, string nome)
    {
        var resposta = await http.PostAsJsonAsync("/centros-de-custo", new DadosDoCentro(nome), Json);
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<CentroNaLista>(Json))!.Id;
    }

    private static async Task<string> CampoRecusado(HttpResponseMessage resposta)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>(Json);
        return Assert.Single(corpo!.Problemas).Campo;
    }
}
