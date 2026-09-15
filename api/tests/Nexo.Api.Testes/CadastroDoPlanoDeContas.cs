using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// As categorias em árvore e os centros de custo, pela borda HTTP.
///
/// <para>
/// O que se confere é o que estragaria a classificação sem erro nenhum:
/// categoria a pagar debaixo de uma a receber, galho que passa de três níveis ao
/// ser movido, categoria que vira mãe de si mesma, e duas irmãs com o mesmo nome
/// dividindo o que devia estar numa só.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class CadastroDoPlanoDeContas(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Filha_herda_a_natureza_e_vem_logo_depois_da_de_cima_com_o_caminho()
    {
        var http = await Entrar();

        var pessoal = await Criar(http, "Pessoal", NaturezaLancamento.Pagar);
        var encargos = await Criar(http, "Encargos", default, pessoal);
        await Criar(http, "FGTS", default, encargos);
        await Criar(http, "Honorários", NaturezaLancamento.Receber);

        var lista = await Listar(http);

        /* A receber antes de a pagar, e cada filha logo depois da mãe. */
        Assert.Equal(
            ["Honorários", "Pessoal", "Pessoal › Encargos", "Pessoal › Encargos › FGTS"],
            lista.Select(categoria => categoria.Caminho).ToList());
        Assert.Equal([1, 1, 2, 3], lista.Select(categoria => categoria.Nivel).ToList());
        Assert.All(lista.Skip(1), categoria => Assert.Equal(NaturezaLancamento.Pagar, categoria.Natureza));
    }

    [Fact]
    public async Task Quarto_nivel_e_natureza_diferente_da_de_cima_sao_recusados()
    {
        var http = await Entrar();

        var pessoal = await Criar(http, "Pessoal", NaturezaLancamento.Pagar);
        var encargos = await Criar(http, "Encargos", default, pessoal);
        var fgts = await Criar(http, "FGTS", default, encargos);

        var quarto = await http.PostAsJsonAsync("/categorias", new DadosDaCategoria("Multa", default, fgts), Json);
        Assert.Equal("paiId", await CampoRecusado(quarto));

        /* Receita debaixo de despesa faria o total de despesas somar receita. */
        var trocada = await http.PostAsJsonAsync("/categorias",
            new DadosDaCategoria("Reembolso", NaturezaLancamento.Receber, pessoal), Json);
        Assert.Equal("natureza", await CampoRecusado(trocada));
    }

    [Fact]
    public async Task Nome_se_repete_em_outro_ramo_mas_nao_entre_irmas()
    {
        var http = await Entrar();

        var pessoal = await Criar(http, "Pessoal", NaturezaLancamento.Pagar);
        var ocupacao = await Criar(http, "Ocupação", NaturezaLancamento.Pagar);
        await Criar(http, "Outros", default, pessoal);

        var irma = await http.PostAsJsonAsync("/categorias", new DadosDaCategoria("outros", default, pessoal), Json);
        Assert.Equal("nome", await CampoRecusado(irma));

        var outroRamo = await http.PostAsJsonAsync("/categorias", new DadosDaCategoria("Outros", default, ocupacao), Json);
        Assert.Equal(HttpStatusCode.Created, outroRamo.StatusCode);
    }

    [Fact]
    public async Task Categoria_nao_vai_para_dentro_de_si_mesma_nem_leva_o_galho_alem_de_tres_niveis()
    {
        var http = await Entrar();

        var pessoal = await Criar(http, "Pessoal", NaturezaLancamento.Pagar);
        var encargos = await Criar(http, "Encargos", default, pessoal);
        var ocupacao = await Criar(http, "Ocupação", NaturezaLancamento.Pagar);
        var aluguel = await Criar(http, "Aluguel", default, ocupacao);

        var dentroDeSi = await http.PutAsJsonAsync($"/categorias/{pessoal}",
            new DadosDaCategoria("Pessoal", NaturezaLancamento.Pagar, encargos), Json);
        Assert.Equal("paiId", await CampoRecusado(dentroDeSi));

        /* Pessoal leva Encargos junto: debaixo de Aluguel, Encargos ficaria no quarto nível. */
        var fundoDemais = await http.PutAsJsonAsync($"/categorias/{pessoal}",
            new DadosDaCategoria("Pessoal", NaturezaLancamento.Pagar, aluguel), Json);
        Assert.Equal("paiId", await CampoRecusado(fundoDemais));

        /* E um movimento que cabe passa. */
        var cabe = await http.PutAsJsonAsync($"/categorias/{pessoal}",
            new DadosDaCategoria("Pessoal", NaturezaLancamento.Pagar, ocupacao), Json);
        Assert.Equal(HttpStatusCode.OK, cabe.StatusCode);
        Assert.Contains("Ocupação › Pessoal › Encargos", (await Listar(http)).Select(categoria => categoria.Caminho));
    }

    [Fact]
    public async Task Plano_sugerido_so_entra_num_plano_vazio()
    {
        var http = await Entrar();

        var primeira = await http.PostAsync("/categorias/plano-sugerido", null);
        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);

        var lista = await Listar(http);
        Assert.Contains("Ocupação › Aluguel e condomínio", lista.Select(categoria => categoria.Caminho));
        Assert.Contains(lista, categoria => categoria.Natureza == NaturezaLancamento.Receber);

        var segunda = await http.PostAsync("/categorias/plano-sugerido", null);
        Assert.Equal("categorias", await CampoRecusado(segunda));
        Assert.Equal(lista.Count, (await Listar(http)).Count);
    }

    [Fact]
    public async Task Categoria_de_outro_escritorio_nao_se_altera()
    {
        var http = await Entrar();
        var outroEscritorio = await Entrar();
        var pessoal = await Criar(http, "Pessoal", NaturezaLancamento.Pagar);

        var resposta = await outroEscritorio.PutAsJsonAsync($"/categorias/{pessoal}",
            new DadosDaCategoria("Tomada", NaturezaLancamento.Pagar, null), Json);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal("Pessoal", Assert.Single(await Listar(http)).Nome);
    }

    [Fact]
    public async Task Centro_de_custo_tem_nome_unico_e_se_inativa_sem_sumir()
    {
        var http = await Entrar();

        var matriz = await http.PostAsJsonAsync("/centros-de-custo", new DadosDoCentro("Matriz"), Json);
        Assert.Equal(HttpStatusCode.Created, matriz.StatusCode);
        var matrizId = (await matriz.Content.ReadFromJsonAsync<CentroNaLista>(Json))!.Id;

        var repetido = await http.PostAsJsonAsync("/centros-de-custo", new DadosDoCentro("matriz"), Json);
        Assert.Equal("nome", await CampoRecusado(repetido));

        (await http.PostAsJsonAsync("/centros-de-custo", new DadosDoCentro("Filial"), Json)).EnsureSuccessStatusCode();

        var inativar = await http.PutAsJsonAsync($"/centros-de-custo/{matrizId}", new DadosDoCentro("Matriz", false), Json);
        Assert.Equal(HttpStatusCode.OK, inativar.StatusCode);

        /* Ativos primeiro: pela ordem do nome, Filial viria antes de qualquer jeito, então confere a marca. */
        var centros = await http.GetFromJsonAsync<List<CentroNaLista>>("/centros-de-custo", Json);
        Assert.Equal([("Filial", true), ("Matriz", false)], centros!.Select(centro => (centro.Nome, centro.Ativo)).ToList());
    }

    /* --------------------------------------------------------- apoio */

    private async Task<HttpClient> Entrar() =>
        await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

    private static async Task<Guid> Criar(HttpClient http, string nome, NaturezaLancamento natureza, Guid? pai = null)
    {
        var resposta = await http.PostAsJsonAsync("/categorias", new DadosDaCategoria(nome, natureza, pai), Json);
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<CategoriaNaLista>(Json))!.Id;
    }

    private static async Task<List<CategoriaNaLista>> Listar(HttpClient http) =>
        (await http.GetFromJsonAsync<List<CategoriaNaLista>>("/categorias", Json))!;

    private static async Task<string> CampoRecusado(HttpResponseMessage resposta)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>(Json);
        return Assert.Single(corpo!.Problemas).Campo;
    }
}
