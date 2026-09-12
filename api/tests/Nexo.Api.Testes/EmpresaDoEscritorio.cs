using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// Corrigir o próprio estabelecimento.
///
/// <para>
/// O provisionamento grava razão social e CNPJ uma vez, lendo variável de
/// ambiente, e não conferia nada. Um dígito trocado ali ficava gravado para
/// sempre: não havia <c>PUT</c>, não havia tela, e reprovisionar não resolve
/// porque só age com o banco vazio.
/// </para>
/// <para>
/// O que estes testes guardam é o conserto e os seus limites — inclusive que
/// salvar de novo o mesmo CNPJ não pode acusar duplicidade consigo mesma, que
/// é o jeito mais fácil de esta regra nascer quebrada.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class EmpresaDoEscritorio(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Corrige_razao_social_e_cnpj()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var empresa = await Primeira(http);
        var cnpj = Documentos.CnpjValido();

        var resposta = await http.PutAsJsonAsync($"/empresas/{empresa.Id}",
            new DadosDeEmpresa("Escritório Asato Contabilidade Ltda", "Asato Contabilidade", cnpj), Json);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        /* A leitura seguinte é que prova: a resposta poderia mentir sem gravar. */
        var depois = await Primeira(http);
        Assert.Equal("Escritório Asato Contabilidade Ltda", depois.RazaoSocial);
        Assert.Equal("Asato Contabilidade", depois.NomeFantasia);
        Assert.Equal(cnpj, depois.Cnpj);
    }

    [Fact]
    public async Task Cnpj_com_digito_errado_e_recusado()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var empresa = await Primeira(http);

        var resposta = await http.PutAsJsonAsync($"/empresas/{empresa.Id}",
            new DadosDeEmpresa("Escritório qualquer", null, "11222333000180"), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);

        var problema = Assert.Single(await Problemas(resposta));
        Assert.Equal("cnpj", problema.Campo);

        /* Recusar e gravar assim mesmo seria o pior dos dois mundos. */
        Assert.Equal(empresa.RazaoSocial, (await Primeira(http)).RazaoSocial);
    }

    [Fact]
    public async Task Razao_social_em_branco_e_recusada()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var empresa = await Primeira(http);

        var resposta = await http.PutAsJsonAsync($"/empresas/{empresa.Id}",
            new DadosDeEmpresa("   ", null, Documentos.CnpjValido()), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal("razaoSocial", Assert.Single(await Problemas(resposta)).Campo);
    }

    [Fact]
    public async Task Salvar_de_novo_o_mesmo_cnpj_nao_acusa_duplicidade()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var empresa = await Primeira(http);
        var cnpj = Documentos.CnpjValido();

        var primeira = await http.PutAsJsonAsync($"/empresas/{empresa.Id}",
            new DadosDeEmpresa("Escritório Asato", null, cnpj), Json);
        primeira.EnsureSuccessStatusCode();

        /*
         * Segunda gravação com o mesmo documento, que é o caso comum: abrir a
         * tela, corrigir só o nome e salvar. A busca por duplicidade precisa
         * excluir a própria empresa, ou a regra impede de salvar qualquer coisa
         * depois da primeira vez.
         */
        var segunda = await http.PutAsJsonAsync($"/empresas/{empresa.Id}",
            new DadosDeEmpresa("Escritório Asato Contabilidade", null, cnpj), Json);

        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        Assert.Equal("Escritório Asato Contabilidade", (await Primeira(http)).RazaoSocial);
    }

    [Fact]
    public async Task Um_escritorio_nao_altera_a_empresa_do_outro()
    {
        var httpA = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var contaB = await Contas.Criar(banco, _aplicacao);
        var httpB = await Contas.Entrar(_aplicacao, contaB);

        var empresaB = await Primeira(httpB);

        var resposta = await httpA.PutAsJsonAsync($"/empresas/{empresaB.Id}",
            new DadosDeEmpresa("Invadido", null, Documentos.CnpjValido()), Json);

        /*
         * Não é 403 nem 404 escrito à mão: a consulta não tem filtro de tenant
         * nenhum, e a empresa do vizinho simplesmente não existe para esta
         * sessão. Quem recusa é a política de RLS no Postgres.
         */
        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal(contaB.RazaoSocial, (await Primeira(httpB)).RazaoSocial);
    }

    /* --------------------------------------------------------- apoio */

    private static async Task<List<Problema>> Problemas(HttpResponseMessage resposta) =>
        (await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>(Json))!.Problemas;

    private static async Task<EmpresaNaLista> Primeira(HttpClient http) =>
        (await http.GetFromJsonAsync<List<EmpresaNaLista>>("/empresas", Json))!.Single();
}
