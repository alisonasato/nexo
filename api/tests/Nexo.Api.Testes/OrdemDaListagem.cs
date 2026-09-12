using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// A ordem da listagem de pessoas.
///
/// <para>
/// <b>Ordenar é do banco, não da tela.</b> Com 25 linhas de 300, classificar no
/// navegador produziria uma ordem perfeita dentro de um recorte arbitrário, e a
/// página 2 traria nomes que deveriam vir antes dos da página 1. Estes testes
/// existem para que a ordem continue vindo pronta.
/// </para>
/// <para>
/// O caso que engana é o código: ele é número guardado como texto, e em texto
/// "10" vem antes de "2".
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class OrdemDaListagemDePessoas(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Por_padrao_vem_em_ordem_de_nome()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        await CriarTres(http);

        Assert.Equal(["Alfa", "Beta", "Gama"], await Nomes(http, ""));
    }

    [Fact]
    public async Task Nome_decrescente_inverte()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        await CriarTres(http);

        Assert.Equal(["Gama", "Beta", "Alfa"], await Nomes(http, "?ordenarPor=Nome&direcao=Decrescente"));
    }

    [Fact]
    public async Task Codigo_ordena_como_numero_e_nao_como_texto()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        /*
         * Onze cadastros levam o código até 11, que é onde a ordem de texto
         * quebra: ela devolveria 1, 10, 11, 2, 3… Com dez ou menos, os dois
         * jeitos concordam e o teste passaria sem provar nada.
         */
        for (var i = 1; i <= 11; i++)
        {
            var resposta = await http.PostAsJsonAsync("/pessoas", Dados($"Pessoa {i:00}"), Json);
            resposta.EnsureSuccessStatusCode();
        }

        var crescente = await Codigos(http, "?ordenarPor=Codigo&tamanho=50");
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11"], crescente);

        var decrescente = await Codigos(http, "?ordenarPor=Codigo&direcao=Decrescente&tamanho=50");
        Assert.Equal(["11", "10", "9", "8", "7", "6", "5", "4", "3", "2", "1"], decrescente);
    }

    [Fact]
    public async Task A_ordem_vale_sobre_a_lista_inteira_e_nao_sobre_a_pagina()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        foreach (var nome in new[] { "Zulu", "Alfa", "Mike", "Bravo" })
        {
            var resposta = await http.PostAsJsonAsync("/pessoas", Dados(nome), Json);
            resposta.EnsureSuccessStatusCode();
        }

        /*
         * Duas por página. Ordenar depois de paginar devolveria "Alfa, Zulu" na
         * primeira — a ordem certa das duas erradas. A primeira página precisa
         * trazer as duas primeiras da lista toda.
         */
        Assert.Equal(["Alfa", "Bravo"], await Nomes(http, "?tamanho=2&pagina=1"));
        Assert.Equal(["Mike", "Zulu"], await Nomes(http, "?tamanho=2&pagina=2"));
    }

    /* --------------------------------------------------------- apoio */

    private static async Task CriarTres(HttpClient http)
    {
        foreach (var nome in new[] { "Beta", "Gama", "Alfa" })
        {
            var resposta = await http.PostAsJsonAsync("/pessoas", Dados(nome), Json);
            resposta.EnsureSuccessStatusCode();
        }
    }

    private static async Task<List<string>> Nomes(HttpClient http, string consulta) =>
        (await Pagina(http, consulta)).Itens.Select(pessoa => pessoa.Nome).ToList();

    private static async Task<List<string>> Codigos(HttpClient http, string consulta) =>
        (await Pagina(http, consulta)).Itens.Select(pessoa => pessoa.Codigo).ToList();

    private static async Task<PaginaDePessoas> Pagina(HttpClient http, string consulta) =>
        (await http.GetFromJsonAsync<PaginaDePessoas>("/pessoas" + consulta, Json))!;

    private static DadosDePessoa Dados(string nome) => new(
        TipoPessoa.Juridica, RegimeTributario.SimplesNacional, string.Empty, [Papel.Cliente],
        nome, string.Empty, Documentos.CnpjValido(), string.Empty, string.Empty,
        string.Empty, string.Empty, string.Empty, null, string.Empty, true);
}
