using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// Os papéis de uma pessoa: cliente, fornecedor, vendedor, colaborador.
///
/// <para>
/// <b>Um cadastro só, com rótulos.</b> Havia uma tabela <c>clientes</c>
/// apontando para <c>pessoas</c>, e ela carregava código, regime tributário e
/// responsável — nenhum dos três era do vínculo. O que sobrou de verdadeiro do
/// relacionamento cabe numa linha que existe ou não existe.
/// </para>
/// <para>
/// O que estes testes guardam é a propriedade que motivou a mudança: a mesma
/// pessoa acumula papéis sem duplicar cadastro, e perder um papel não apaga
/// ninguém.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class PapeisDaPessoa(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task A_mesma_pessoa_e_cliente_e_fornecedora_sem_duplicar_cadastro()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        var criada = await http.PostAsJsonAsync("/pessoas",
            Dados("Gráfica que imprime os carnês", [Papel.Cliente, Papel.Fornecedor]), Json);

        criada.EnsureSuccessStatusCode();
        var pessoa = await criada.Content.ReadFromJsonAsync<PessoaDetalhada>(Json);

        Assert.Equal([Papel.Cliente, Papel.Fornecedor], pessoa!.Papeis);

        /*
         * O caso que derrubava o desenho antigo: a gráfica imprime os carnês do
         * escritório e é cliente dele. Com tabela por papel, isso viravam dois
         * cadastros com o mesmo CNPJ.
         */
        var porCliente = await Listar(http, Papel.Cliente);
        var porFornecedor = await Listar(http, Papel.Fornecedor);

        Assert.Equal(pessoa.Id, Assert.Single(porCliente).Id);
        Assert.Equal(pessoa.Id, Assert.Single(porFornecedor).Id);
    }

    [Fact]
    public async Task Filtrar_por_papel_deixa_de_fora_quem_nao_tem()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        await http.PostAsJsonAsync("/pessoas", Dados("Um cliente", [Papel.Cliente]), Json);
        await http.PostAsJsonAsync("/pessoas", Dados("Um vendedor", [Papel.Vendedor]), Json);
        await http.PostAsJsonAsync("/pessoas", Dados("Sem papel nenhum", []), Json);

        Assert.Equal("Um cliente", Assert.Single(await Listar(http, Papel.Cliente)).Nome);
        Assert.Equal("Um vendedor", Assert.Single(await Listar(http, Papel.Vendedor)).Nome);
        Assert.Empty(await Listar(http, Papel.Colaborador));

        /* Sem filtro, aparecem os três: pessoa sem papel continua no cadastro. */
        var todas = await http.GetFromJsonAsync<PaginaDePessoas>("/pessoas", Json);
        Assert.Equal(3, todas!.Total);
    }

    [Fact]
    public async Task Tirar_um_papel_nao_apaga_a_pessoa()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        var criada = await http.PostAsJsonAsync("/pessoas",
            Dados("Deixou de ser cliente", [Papel.Cliente, Papel.Fornecedor]), Json);
        var pessoa = await criada.Content.ReadFromJsonAsync<PessoaDetalhada>(Json);

        var alterada = await http.PutAsJsonAsync($"/pessoas/{pessoa!.Id}",
            Dados("Deixou de ser cliente", [Papel.Fornecedor]), Json);

        alterada.EnsureSuccessStatusCode();
        var depois = await alterada.Content.ReadFromJsonAsync<PessoaDetalhada>(Json);

        /*
         * Perder o rótulo é deixar de ser cliente. A pessoa segue ativa e no
         * cadastro, com o mesmo código — é diferente de inativar, que é sair.
         */
        Assert.Equal([Papel.Fornecedor], depois!.Papeis);
        Assert.True(depois.Ativo);
        Assert.Equal(pessoa.Codigo, depois.Codigo);
    }

    [Fact]
    public async Task Papel_repetido_no_pedido_vira_um_so()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        var criada = await http.PostAsJsonAsync("/pessoas",
            Dados("Cliente insistente", [Papel.Cliente, Papel.Cliente, Papel.Cliente]), Json);

        criada.EnsureSuccessStatusCode();
        var pessoa = await criada.Content.ReadFromJsonAsync<PessoaDetalhada>(Json);

        /* A chave é o par pessoa e papel: repetir não cria linha, e o pedido
           repetido não pode virar erro de banco na cara de quem cadastra. */
        Assert.Equal([Papel.Cliente], pessoa!.Papeis);
    }

    [Fact]
    public async Task Papeis_de_um_escritorio_nao_aparecem_no_outro()
    {
        var httpA = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var httpB = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        await httpA.PostAsJsonAsync("/pessoas", Dados("Cliente do A", [Papel.Cliente]), Json);

        Assert.Single(await Listar(httpA, Papel.Cliente));
        Assert.Empty(await Listar(httpB, Papel.Cliente));
    }

    [Fact]
    public async Task A_tabela_de_papeis_obedece_a_politica_de_isolamento()
    {
        var conta = await Contas.Criar(banco, _aplicacao);
        var http = await Contas.Entrar(_aplicacao, conta);

        await http.PostAsJsonAsync("/pessoas", Dados("Com papel", [Papel.Colaborador]), Json);

        /*
         * Sem tenant declarado, a política esconde tudo. É a mesma prova que as
         * outras tabelas têm, e ela precisa existir aqui também: tabela nova
         * nasce com RLS, ou nasce com um buraco.
         */
        await using (var semTenant = banco.Criar(tenant: null))
            Assert.Empty(await semTenant.PessoaPapeis.ToListAsync());

        await using (var comTenant = banco.Criar(conta.TenantId))
            Assert.Single(await comTenant.PessoaPapeis.ToListAsync());
    }

    /* ------------------------------------------------------------- apoio */

    private static async Task<List<PessoaNaLista>> Listar(HttpClient http, Papel papel)
    {
        var pagina = await http.GetFromJsonAsync<PaginaDePessoas>($"/pessoas?papel={papel}", Json);
        return pagina!.Itens.ToList();
    }

    private static DadosDePessoa Dados(string nome, Papel[] papeis) => new(
        TipoPessoa.Juridica, RegimeTributario.SimplesNacional, "Shoiti", papeis,
        nome, string.Empty, Documentos.CnpjValido(), string.Empty, string.Empty,
        string.Empty, string.Empty, string.Empty, null, string.Empty, true);
}
