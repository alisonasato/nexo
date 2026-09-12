using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// O CNPJ com letras.
///
/// <para>
/// Desde <b>31 de julho de 2026</b> a Receita emite CNPJ alfanumérico para
/// inscrição nova (IN RFB 2.229/2024). As 12 primeiras posições aceitam
/// <c>0-9</c> e <c>A-Z</c>; os 2 dígitos verificadores continuam numéricos, e
/// quem já tem CNPJ não muda de número.
/// </para>
/// <para>
/// Antes disto o sistema limpava todo documento para dígitos. Um cliente novo
/// com letras no CNPJ não era recusado com mensagem: o documento era
/// silenciosamente encurtado e gravado errado, o que é pior.
/// </para>
/// <para>
/// O número que prova o cálculo é <c>12ABC34501DE35</c>, o exemplo oficial do
/// Serpro. Ele vem de fora de propósito — um caso construído pela própria
/// implementação só provaria que ela concorda consigo mesma.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class CnpjAlfanumerico(BancoDeTestes banco) : IDisposable
{
    private const string ExemploDoSerpro = "12ABC34501DE35";

    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Cadastra_pessoa_juridica_com_letras_no_cnpj()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        var resposta = await http.PostAsJsonAsync("/pessoas",
            Dados("Empresa de inscrição nova", ExemploDoSerpro), Json);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);

        var pessoa = await resposta.Content.ReadFromJsonAsync<PessoaDetalhada>(Json);
        Assert.Equal(ExemploDoSerpro, pessoa!.Documento);
    }

    [Fact]
    public async Task O_cnpj_formatado_e_o_minusculo_chegam_no_mesmo_documento()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        var comPontuacao = await http.PostAsJsonAsync("/pessoas",
            Dados("Com pontuação", "12.ABC.345/01DE-35"), Json);
        comPontuacao.EnsureSuccessStatusCode();

        /*
         * Minúscula não é documento diferente: quem digita "de" no lugar de
         * "DE" escreveu o CNPJ certo com a tecla errada. Vira maiúscula antes
         * de qualquer conferência — inclusive a de duplicidade, que é o que
         * este segundo cadastro testa.
         */
        var minusculo = await http.PostAsJsonAsync("/pessoas",
            Dados("Em minúsculas", "12abc34501de35"), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, minusculo.StatusCode);

        var problema = Assert.Single(await Problemas(minusculo));
        Assert.Equal("documento", problema.Campo);
        Assert.Contains("Com pontuação", problema.Descricao);
    }

    [Fact]
    public async Task Digito_verificador_errado_e_recusado()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        /* O mesmo número do Serpro, com o último dígito trocado. */
        var resposta = await http.PostAsJsonAsync("/pessoas",
            Dados("Empresa com dígito trocado", "12ABC34501DE34"), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal("documento", Assert.Single(await Problemas(resposta)).Campo);
    }

    [Fact]
    public async Task Letra_no_lugar_do_digito_verificador_e_recusada()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        /*
         * A regra não é "aceita letra em qualquer lugar": as duas últimas
         * posições continuam numéricas. Sem esta conferência, a conta do módulo
         * 11 aceitaria a letra sem reclamar, porque para ela letra é só outro
         * número.
         */
        var resposta = await http.PostAsJsonAsync("/pessoas",
            Dados("Empresa com letra no fim", "12ABC34501DEA5"), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal("documento", Assert.Single(await Problemas(resposta)).Campo);
    }

    [Fact]
    public async Task A_busca_acha_pelo_cnpj_com_letras()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        await http.PostAsJsonAsync("/pessoas", Dados("Quem tem letra", ExemploDoSerpro), Json);
        await http.PostAsJsonAsync("/pessoas", Dados("Quem não tem", Documentos.CnpjValido()), Json);

        /*
         * Limpar o termo para dígitos deixaria "123450135", que não é o
         * documento de ninguém: a busca por CNPJ alfanumérico não acharia nada.
         */
        var achados = await Buscar(http, "12ABC34501DE35");
        Assert.Equal("Quem tem letra", Assert.Single(achados).Nome);

        /* E com a pontuação que a tela mostra, também. */
        Assert.Single(await Buscar(http, "12.ABC.345/01DE-35"));
    }

    [Fact]
    public async Task O_cnpj_numerico_de_sempre_continua_valendo()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var numerico = Documentos.CnpjValido();

        var resposta = await http.PostAsJsonAsync("/pessoas",
            Dados("Empresa de inscrição antiga", numerico), Json);

        /*
         * Quem já tem CNPJ não muda de número. Uma mudança que fizesse o
         * cadastro existente parar de valer seria muito pior do que não aceitar
         * o formato novo.
         */
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.Equal(numerico, (await resposta.Content.ReadFromJsonAsync<PessoaDetalhada>(Json))!.Documento);
    }

    [Fact]
    public async Task A_empresa_do_escritorio_tambem_aceita_letras()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var empresa = (await http.GetFromJsonAsync<List<EmpresaNaLista>>("/empresas", Json))!.Single();

        var resposta = await http.PutAsJsonAsync($"/empresas/{empresa.Id}",
            new DadosDeEmpresa("Escritório de inscrição nova", null, ExemploDoSerpro), Json);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        var depois = (await http.GetFromJsonAsync<List<EmpresaNaLista>>("/empresas", Json))!.Single();
        Assert.Equal(ExemploDoSerpro, depois.Cnpj);
    }

    /* --------------------------------------------------------- apoio */

    private static async Task<List<Problema>> Problemas(HttpResponseMessage resposta) =>
        (await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>(Json))!.Problemas;

    private static async Task<List<PessoaNaLista>> Buscar(HttpClient http, string termo) =>
        (await http.GetFromJsonAsync<PaginaDePessoas>($"/pessoas?busca={Uri.EscapeDataString(termo)}", Json))!.Itens;

    private static DadosDePessoa Dados(string nome, string documento) => new(
        TipoPessoa.Juridica, RegimeTributario.SimplesNacional, string.Empty, [Papel.Cliente],
        nome, string.Empty, documento, string.Empty, string.Empty,
        string.Empty, string.Empty, string.Empty, null, string.Empty, true);
}
