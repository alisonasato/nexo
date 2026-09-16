using System.Net.Http.Json;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;
using static Nexo.Api.Testes.Apoio;

namespace Nexo.Api.Testes;

/// <summary>
/// Os filtros de categoria e de centro de custo na listagem de lançamentos.
///
/// <para>
/// Ao contrário da busca, eles recortam também os totais: a categoria escolhe
/// que parte do dinheiro medir. Os testes conferem que o recorte leva junto as
/// categorias de baixo, e que "sem categoria" acha o que ficou por classificar.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class FiltrosDaClassificacao(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Filtrar_por_categoria_leva_as_de_baixo_e_recorta_os_totais()
    {
        var http = await Entrar();
        var pessoal = await CriarCategoria(http, "Pessoal", null);
        var salarios = await CriarCategoria(http, "Salários", pessoal);
        var encargos = await CriarCategoria(http, "Encargos", pessoal);
        var ocupacao = await CriarCategoria(http, "Ocupação", null);

        await Lancar(http, 1_000m, salarios, null);
        await Lancar(http, 300m, encargos, null);
        await Lancar(http, 500m, ocupacao, null);

        var dePessoal = await Listar(http, $"categoriaId={pessoal}");

        Assert.Equal(2, dePessoal.Itens.Count);
        Assert.Equal(1_300m, dePessoal.TotalEmAberto);

        /* A de baixo sozinha traz só ela. */
        var deEncargos = await Listar(http, $"categoriaId={encargos}");
        Assert.Equal(300m, Assert.Single(deEncargos.Itens).Valor);
        Assert.Equal(300m, deEncargos.TotalEmAberto);
    }

    [Fact]
    public async Task Sem_categoria_acha_o_que_ficou_por_classificar()
    {
        var http = await Entrar();
        var ocupacao = await CriarCategoria(http, "Ocupação", null);

        await Lancar(http, 500m, ocupacao, null);
        await Lancar(http, 80m, null, null);

        var semCategoria = await Listar(http, "semCategoria=true");

        Assert.Equal(80m, Assert.Single(semCategoria.Itens).Valor);
        Assert.Equal(80m, semCategoria.TotalEmAberto);
    }

    [Fact]
    public async Task Filtrar_por_centro_de_custo_traz_so_o_dele()
    {
        var http = await Entrar();
        var matriz = await CriarCentro(http, "Matriz");
        var filial = await CriarCentro(http, "Filial");

        await Lancar(http, 100m, null, matriz);
        await Lancar(http, 200m, null, filial);
        await Lancar(http, 40m, null, null);

        var daMatriz = await Listar(http, $"centroDeCustoId={matriz}");
        Assert.Equal(100m, Assert.Single(daMatriz.Itens).Valor);
        Assert.Equal(100m, daMatriz.TotalEmAberto);

        var semCentro = await Listar(http, "semCentroDeCusto=true");
        Assert.Equal(40m, Assert.Single(semCentro.Itens).Valor);
    }

    /* --------------------------------------------------------- apoio */

    private async Task<HttpClient> Entrar() =>
        await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

    private static async Task<Guid> CriarCategoria(HttpClient http, string nome, Guid? pai)
    {
        var resposta = await http.PostAsJsonAsync("/categorias",
            new DadosDaCategoria(nome, pai is null ? NaturezaLancamento.Pagar : default, pai), Json);
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<CategoriaNaLista>(Json))!.Id;
    }

    private static async Task<Guid> CriarCentro(HttpClient http, string nome)
    {
        var resposta = await http.PostAsJsonAsync("/centros-de-custo", new DadosDoCentro(nome), Json);
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<CentroNaLista>(Json))!.Id;
    }

    /// <summary>Um lançamento a pagar, com um fornecedor novo, na competência de março.</summary>
    private static async Task Lancar(HttpClient http, decimal valor, Guid? categoria, Guid? centro)
    {
        var pessoa = await http.PostAsJsonAsync("/pessoas", new DadosDePessoa(
            TipoPessoa.Juridica, RegimeTributario.SimplesNacional, string.Empty, [Papel.Fornecedor],
            "Fornecedor do teste", string.Empty, Documentos.CnpjValido(), string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, null, string.Empty, true), Json);
        pessoa.EnsureSuccessStatusCode();
        var pessoaId = (await pessoa.Content.ReadFromJsonAsync<PessoaDetalhada>(Json))!.Id;

        var lancamento = await http.PostAsJsonAsync("/lancamentos", new DadosDoAvulso(
            NaturezaLancamento.Pagar, pessoaId, "Conta do teste", valor, new DateOnly(2026, 3, 10), 2026, 3,
            categoria, centro), Json);
        lancamento.EnsureSuccessStatusCode();
    }

    private static async Task<PaginaDeLancamentos> Listar(HttpClient http, string filtro) =>
        (await http.GetFromJsonAsync<PaginaDeLancamentos>(
            $"/lancamentos?natureza=Pagar&ano=2026&mes=3&{filtro}", Json))!;
}
