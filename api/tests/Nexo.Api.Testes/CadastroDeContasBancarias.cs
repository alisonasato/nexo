using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// O cadastro de contas bancárias pela borda HTTP: validação, nome único no
/// escritório, inativação e isolamento.
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class CadastroDeContasBancarias(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Conta_cadastrada_aparece_na_lista_com_o_saldo_inicial()
    {
        var http = await Entrar();

        var resposta = await http.PostAsJsonAsync("/contas-bancarias", Dados("Itaú PJ", saldo: -250.40m), Json);
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);

        var conta = Assert.Single(await Listar(http));
        Assert.Equal("Itaú PJ", conta.Nome);
        Assert.Equal(-250.40m, conta.SaldoInicial);
        Assert.Equal(new DateOnly(2026, 9, 1), conta.SaldoInicialEm);
        Assert.True(conta.Ativa);
    }

    [Fact]
    public async Task Nome_se_repete_entre_escritorios_mas_nao_dentro_de_um()
    {
        var http = await Entrar();
        var outroEscritorio = await Entrar();

        (await http.PostAsJsonAsync("/contas-bancarias", Dados("Caixa"), Json)).EnsureSuccessStatusCode();

        /* Maiúscula não diferencia: ninguém escolhe entre "Caixa" e "caixa" na hora da baixa. */
        var repetida = await http.PostAsJsonAsync("/contas-bancarias", Dados("caixa"), Json);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, repetida.StatusCode);
        Assert.Equal("nome", (await ProblemaUnico(repetida)).Campo);

        var noOutro = await outroEscritorio.PostAsJsonAsync("/contas-bancarias", Dados("Caixa"), Json);
        Assert.Equal(HttpStatusCode.Created, noOutro.StatusCode);
    }

    [Fact]
    public async Task Conta_sem_nome_tipo_ou_dia_do_saldo_e_recusada()
    {
        var http = await Entrar();

        var resposta = await http.PostAsJsonAsync("/contas-bancarias", new { nome = " ", saldoInicial = 100m }, Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);

        var campos = (await ProblemasDa(resposta)).Select(problema => problema.Campo).ToList();
        Assert.Contains("nome", campos);
        Assert.Contains("tipo", campos);
        Assert.Contains("saldoInicialEm", campos);
    }

    [Fact]
    public async Task Alterar_mantem_o_proprio_nome_e_inativa_sem_apagar()
    {
        var http = await Entrar();

        var asaas = await Lido(await http.PostAsJsonAsync("/contas-bancarias", Dados("Asaas", TipoConta.Pagamento), Json));
        (await http.PostAsJsonAsync("/contas-bancarias", Dados("Bradesco"), Json)).EnsureSuccessStatusCode();

        /* Salvar sem mexer no nome não pode acusar duplicidade consigo mesma. */
        var alterada = await http.PutAsJsonAsync($"/contas-bancarias/{asaas.Id}",
            Dados("Asaas", TipoConta.Pagamento, ativa: false) with { Agencia = "0002" }, Json);
        Assert.Equal(HttpStatusCode.OK, alterada.StatusCode);

        /* Ativas primeiro: pela ordem do nome, Asaas viria antes. */
        var lista = await Listar(http);
        Assert.Equal(["Bradesco", "Asaas"], lista.Select(conta => conta.Nome).ToList());
        Assert.False(lista[1].Ativa);
        Assert.Equal("0002", lista[1].Agencia);
    }

    [Fact]
    public async Task Conta_de_outro_escritorio_nao_se_altera()
    {
        var http = await Entrar();
        var outroEscritorio = await Entrar();
        var conta = await Lido(await http.PostAsJsonAsync("/contas-bancarias", Dados("Itaú PJ"), Json));

        var resposta = await outroEscritorio.PutAsJsonAsync($"/contas-bancarias/{conta.Id}", Dados("Tomada"), Json);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal("Itaú PJ", Assert.Single(await Listar(http)).Nome);
    }

    [Fact]
    public async Task Caixa_em_dinheiro_nao_guarda_banco_agencia_nem_numero()
    {
        var http = await Entrar();

        var conta = await Lido(await http.PostAsJsonAsync("/contas-bancarias",
            Dados("Caixa da recepção", TipoConta.Dinheiro), Json));

        Assert.Equal((string.Empty, string.Empty, string.Empty), (conta.Banco, conta.Agencia, conta.Numero));
    }

    /* --------------------------------------------------------- apoio */

    private async Task<HttpClient> Entrar() =>
        await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

    private static DadosDaConta Dados(
        string nome, TipoConta tipo = TipoConta.Corrente, decimal saldo = 1_000m, bool ativa = true) =>
        new(nome, tipo, "Banco do teste", "0001", "12345-6", saldo, new DateOnly(2026, 9, 1), ativa);

    private static async Task<List<ContaNaLista>> Listar(HttpClient http) =>
        (await http.GetFromJsonAsync<List<ContaNaLista>>("/contas-bancarias", Json))!;

    private static async Task<ContaNaLista> Lido(HttpResponseMessage resposta)
    {
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<ContaNaLista>(Json))!;
    }

    private static async Task<List<Problema>> ProblemasDa(HttpResponseMessage resposta) =>
        (await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>(Json))!.Problemas;

    private static async Task<Problema> ProblemaUnico(HttpResponseMessage resposta) =>
        Assert.Single(await ProblemasDa(resposta));
}
