using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// O cadastro de pessoas pela borda HTTP: validação, isolamento e busca.
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class CadastroDePessoas(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    /*
     * O mesmo conversor que a API usa. Sem ele o cliente de teste lê o enum
     * como número e falha ao receber "Fisica" — que foi exatamente o que estes
     * testes acusaram quando o contrato passou a trafegar texto. Contrato só é
     * contrato quando os dois lados combinam.
     */
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Recusa_cpf_com_digito_verificador_errado()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        var resposta = await cliente.PostAsJsonAsync("/pessoas",
            Dados(TipoPessoa.Fisica, "Maria de Souza", "11111111112"), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);

        var corpo = await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>(Json);
        var problema = Assert.Single(corpo!.Problemas);
        Assert.Equal("documento", problema.Campo);

        // A mensagem tem que dizer o que fazer, não só que está errado.
        Assert.Contains("dígitos verificadores", problema.Sugestao);
    }

    [Fact]
    public async Task Aceita_cadastro_sem_endereco_nenhum()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        var resposta = await cliente.PostAsJsonAsync("/pessoas",
            Dados(TipoPessoa.Fisica, "João Carlos Pereira", CpfValido()), Json);

        /*
         * Cadastro incompleto é o caso comum, não a exceção: o cliente chega
         * por telefone, a consulta de CEP falha, a planilha só tinha o nome.
         * Exigir endereço faria a pessoa inventar dado para o sistema aceitar
         * o dado verdadeiro.
         */
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    }

    [Fact]
    public async Task Recusa_o_mesmo_documento_duas_vezes_no_mesmo_tenant()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var cpf = CpfValido();

        var primeira = await cliente.PostAsJsonAsync("/pessoas", Dados(TipoPessoa.Fisica, "Ana Lima", cpf), Json);
        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);

        var segunda = await cliente.PostAsJsonAsync("/pessoas", Dados(TipoPessoa.Fisica, "Ana Lima", cpf), Json);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, segunda.StatusCode);

        var corpo = await segunda.Content.ReadFromJsonAsync<RespostaComProblemas>(Json);
        Assert.Contains(corpo!.Problemas, problema => problema.Titulo == TitulosDeProblema.Duplicidade);
    }

    [Fact]
    public async Task O_mesmo_documento_pode_existir_em_tenants_diferentes()
    {
        var clienteA = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var clienteB = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var cpf = CpfValido();

        var noPrimeiro = await clienteA.PostAsJsonAsync("/pessoas", Dados(TipoPessoa.Fisica, "Ana Lima", cpf), Json);
        var noSegundo = await clienteB.PostAsJsonAsync("/pessoas", Dados(TipoPessoa.Fisica, "Ana Lima", cpf), Json);

        /*
         * Dois escritórios podem atender o mesmo cliente, e nenhum dos dois
         * pode saber disso. É por isso que o índice único é por tenant e a
         * checagem de duplicidade roda sob a política de RLS — se fosse
         * global, a mensagem "documento já cadastrado" denunciaria a
         * existência de um cadastro alheio.
         */
        Assert.Equal(HttpStatusCode.Created, noPrimeiro.StatusCode);
        Assert.Equal(HttpStatusCode.Created, noSegundo.StatusCode);
    }

    [Fact]
    public async Task Um_tenant_nao_ve_as_pessoas_do_outro()
    {
        var clienteA = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var clienteB = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        await clienteA.PostAsJsonAsync("/pessoas", Dados(TipoPessoa.Fisica, "Cliente do A", CpfValido()), Json);

        var listaDeB = await clienteB.GetFromJsonAsync<PaginaDePessoas>("/pessoas", Json);

        Assert.Empty(listaDeB!.Itens);
        Assert.Equal(0, listaDeB.Total);
    }

    [Fact]
    public async Task A_busca_encontra_pelo_documento_digitado_com_pontuacao()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var cnpj = "11222333000181";

        await cliente.PostAsJsonAsync("/pessoas",
            Dados(TipoPessoa.Juridica, "Comércio de Teste Ltda", cnpj), Json);

        // O documento é guardado sem pontuação; a busca precisa achar assim mesmo.
        var pagina = await cliente.GetFromJsonAsync<PaginaDePessoas>("/pessoas?busca=11.222.333/0001-81", Json);

        var encontrada = Assert.Single(pagina!.Itens);
        Assert.Equal(cnpj, encontrada.Documento);
    }

    [Fact]
    public async Task Inativar_nao_apaga_o_cadastro()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        var criada = await cliente.PostAsJsonAsync("/pessoas",
            Dados(TipoPessoa.Fisica, "Pedro Antunes", CpfValido()), Json);
        var pessoa = await criada.Content.ReadFromJsonAsync<PessoaDetalhada>(Json);

        var remocao = await cliente.DeleteAsync($"/pessoas/{pessoa!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, remocao.StatusCode);

        // Sumiu da lista padrão...
        var lista = await cliente.GetFromJsonAsync<PaginaDePessoas>("/pessoas", Json);
        Assert.Empty(lista!.Itens);

        // ...mas continua existindo, porque quem apareceu num contrato precisa
        // continuar existindo no histórico.
        var comInativos = await cliente.GetFromJsonAsync<PaginaDePessoas>("/pessoas?incluirInativos=true", Json);
        Assert.Single(comInativos!.Itens);
        Assert.False(comInativos.Itens[0].Ativo);
    }

    [Fact]
    public async Task Reativar_traz_a_pessoa_de_volta_para_a_lista()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        var criada = await cliente.PostAsJsonAsync("/pessoas",
            Dados(TipoPessoa.Fisica, "Joana Ribeiro", CpfValido()), Json);
        var pessoa = await criada.Content.ReadFromJsonAsync<PessoaDetalhada>(Json);

        await cliente.DeleteAsync($"/pessoas/{pessoa!.Id}");

        var volta = await cliente.PostAsync($"/pessoas/{pessoa.Id}/reativar", null);
        Assert.Equal(HttpStatusCode.NoContent, volta.StatusCode);

        var lista = await cliente.GetFromJsonAsync<PaginaDePessoas>("/pessoas", Json);
        Assert.True(Assert.Single(lista!.Itens).Ativo);
    }

    [Fact]
    public async Task Reativar_pessoa_de_outro_tenant_devolve_404()
    {
        var clienteA = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var clienteB = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        var criada = await clienteA.PostAsJsonAsync("/pessoas",
            Dados(TipoPessoa.Fisica, "Somente do A", CpfValido()), Json);
        var pessoa = await criada.Content.ReadFromJsonAsync<PessoaDetalhada>(Json);

        await clienteA.DeleteAsync($"/pessoas/{pessoa!.Id}");

        /* O endpoint novo nasce debaixo da mesma política que o resto. */
        var doOutro = await clienteB.PostAsync($"/pessoas/{pessoa.Id}/reativar", null);
        Assert.Equal(HttpStatusCode.NotFound, doOutro.StatusCode);
    }

    [Fact]
    public async Task Sem_sessao_o_cadastro_e_recusado()
    {
        var cliente = _aplicacao.CreateClient();

        var resposta = await cliente.GetAsync("/pessoas");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    /* ------------------------------------------------------------- apoio */

    private static DadosDePessoa Dados(TipoPessoa tipo, string nome, string documento) => new(
        tipo, nome, string.Empty, documento, string.Empty, string.Empty,
        string.Empty, string.Empty, string.Empty, null, string.Empty, true);

    /// <summary>
    /// Um CPF novo com dígitos verificadores certos. Gerar em vez de usar uma
    /// lista fixa mantém os testes independentes: cada um cria o seu, e a
    /// checagem de duplicidade não dispara por acidente.
    /// </summary>
    private static string CpfValido()
    {
        var base9 = Random.Shared.Next(100_000_000, 999_999_999).ToString();

        var primeiro = Digito(base9, 10);
        var segundo = Digito(base9 + primeiro, 11);
        return base9 + primeiro + segundo;

        static int Digito(string numero, int pesoInicial)
        {
            var soma = numero.Select((caractere, indice) => (caractere - '0') * (pesoInicial - indice)).Sum();
            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }
    }
}
