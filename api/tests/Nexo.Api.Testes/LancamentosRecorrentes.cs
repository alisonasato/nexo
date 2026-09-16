using System.Net;
using System.Net.Http.Json;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;
using static Nexo.Api.Testes.Apoio;

namespace Nexo.Api.Testes;

/// <summary>
/// As recorrências sem contrato por trás, e os lançamentos que elas geram.
///
/// <para>
/// O que se confere é o que faria o escritório pagar ou cobrar errado sem
/// perceber: a recorrência a receber de um fornecedor, a frequência contada do
/// mês errado, o dia 31 escorregando de mês, gerar duas vezes o mesmo aluguel, e
/// a recorrência inativa ou vencida que continua gerando.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class LancamentosRecorrentes(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Cadastro_recusa_o_que_geraria_lancamento_errado()
    {
        var http = await Entrar();
        var fornecedor = await CriarPessoa(http, Papel.Fornecedor);
        var valida = Aluguel(fornecedor);

        /* O aluguel a receber de um fornecedor cobraria quem o escritório paga. */
        Assert.Equal("pessoaId", await CampoRecusado(await Cadastrar(http, valida with { Natureza = NaturezaLancamento.Receber })));
        Assert.Equal("valor", await CampoRecusado(await Cadastrar(http, valida with { Valor = 0m })));
        Assert.Equal("diaDeVencimento", await CampoRecusado(await Cadastrar(http, valida with { DiaDeVencimento = 32 })));
        Assert.Equal("fimEm", await CampoRecusado(await Cadastrar(http, valida with { FimEm = new DateOnly(2025, 12, 31) })));

        var criada = await Cadastrar(http, valida);
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);

        var lida = (await criada.Content.ReadFromJsonAsync<RecorrenciaNaLista>(Json))!;
        Assert.Equal((FrequenciaDeRecorrencia.Mensal, true), (lida.Frequencia, lida.Ativa));
    }

    [Fact]
    public async Task A_frequencia_conta_a_partir_do_mes_de_inicio_e_o_dia_cabe_no_mes()
    {
        var http = await Entrar();
        var fornecedor = await CriarPessoa(http, Papel.Fornecedor);

        /* Trimestral desde janeiro, no dia 31: janeiro, abril, julho e outubro. */
        (await Cadastrar(http, Aluguel(fornecedor) with
        {
            Descricao = "Contabilidade da filial",
            Frequencia = FrequenciaDeRecorrencia.Trimestral,
            DiaDeVencimento = 31,
            InicioEm = new DateOnly(2026, 1, 15),
        })).EnsureSuccessStatusCode();

        Assert.Equal(1, (await Gerar(http, 2026, 1)).Gerados);

        var fevereiro = await Gerar(http, 2026, 2);
        Assert.Equal((0, 1), (fevereiro.Gerados, fevereiro.ForaDaVez));

        Assert.Equal(1, (await Gerar(http, 2026, 4)).Gerados);
        Assert.Equal(new DateOnly(2026, 4, 30), Assert.Single((await Listar(http, 2026, 4)).Itens).Vencimento);
    }

    [Fact]
    public async Task Gerar_de_novo_nao_duplica_e_cancelar_libera_a_competencia()
    {
        var http = await Entrar();
        var fornecedor = await CriarPessoa(http, Papel.Fornecedor);
        var ocupacao = await CriarCategoria(http, "Ocupação");

        (await Cadastrar(http, Aluguel(fornecedor) with { CategoriaId = ocupacao })).EnsureSuccessStatusCode();

        Assert.Equal(1, (await Gerar(http, 2026, 3)).Gerados);

        var deNovo = await Gerar(http, 2026, 3);
        Assert.Equal((0, 1), (deNovo.Gerados, deNovo.Ignorados));

        var aluguel = Assert.Single((await Listar(http, 2026, 3)).Itens);
        Assert.Equal("Aluguel do escritório", aluguel.Descricao);
        Assert.Equal(3_000m, aluguel.Valor);
        Assert.Equal(new DateOnly(2026, 3, 10), aluguel.Vencimento);
        Assert.Equal("Ocupação", aluguel.Categoria);

        /* O lançamento errado se cancela e se gera de novo, na mesma competência. */
        (await http.PostAsJsonAsync($"/lancamentos/{aluguel.Id}/cancelar",
            new DadosDoCancelamento("Valor errado"), Json)).EnsureSuccessStatusCode();

        Assert.Equal(1, (await Gerar(http, 2026, 3)).Gerados);
    }

    [Fact]
    public async Task Inativa_ou_fora_da_vigencia_nao_gera_e_a_natureza_nao_muda()
    {
        var http = await Entrar();
        var fornecedor = await CriarPessoa(http, Papel.Fornecedor);

        var criada = await Cadastrar(http, Aluguel(fornecedor) with { FimEm = new DateOnly(2026, 4, 30) });
        var id = (await criada.Content.ReadFromJsonAsync<RecorrenciaNaLista>(Json))!.Id;

        var maio = await Gerar(http, 2026, 5);
        Assert.Equal((0, 1), (maio.Gerados, maio.ForaDaVez));

        /* Inativar pedindo outra natureza no mesmo gesto: a natureza fica, e abril já não gera. */
        var alterada = await http.PutAsJsonAsync($"/recorrencias/{id}", Aluguel(fornecedor) with
        {
            Natureza = NaturezaLancamento.Receber,
            FimEm = new DateOnly(2026, 4, 30),
            Ativa = false,
        }, Json);

        Assert.Equal(HttpStatusCode.OK, alterada.StatusCode);
        var lida = (await alterada.Content.ReadFromJsonAsync<RecorrenciaNaLista>(Json))!;
        Assert.Equal((NaturezaLancamento.Pagar, false), (lida.Natureza, lida.Ativa));

        var abril = await Gerar(http, 2026, 4);
        Assert.Equal((0, 1), (abril.Gerados, abril.ForaDaVez));
    }

    /* --------------------------------------------------------- apoio */

    private async Task<HttpClient> Entrar() =>
        await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

    /// <summary>O aluguel de sempre: a pagar, mensal, dia 10, desde janeiro de 2026.</summary>
    private static DadosDaRecorrencia Aluguel(Guid fornecedor) => new(
        NaturezaLancamento.Pagar, fornecedor, "Aluguel do escritório", 3_000m,
        FrequenciaDeRecorrencia.Mensal, 10, new DateOnly(2026, 1, 1));

    private static Task<HttpResponseMessage> Cadastrar(HttpClient http, DadosDaRecorrencia dados) =>
        http.PostAsJsonAsync("/recorrencias", dados, Json);

    private static async Task<ResultadoDasRecorrencias> Gerar(HttpClient http, int ano, int mes)
    {
        var resposta = await http.PostAsJsonAsync("/recorrencias/gerar", new PedidoDasRecorrencias(ano, mes), Json);
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<ResultadoDasRecorrencias>(Json))!;
    }

    private static async Task<PaginaDeLancamentos> Listar(HttpClient http, int ano, int mes) =>
        (await http.GetFromJsonAsync<PaginaDeLancamentos>($"/lancamentos?natureza=Pagar&ano={ano}&mes={mes}", Json))!;

    private static async Task<Guid> CriarCategoria(HttpClient http, string nome)
    {
        var resposta = await http.PostAsJsonAsync("/categorias",
            new DadosDaCategoria(nome, NaturezaLancamento.Pagar, null), Json);
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<CategoriaNaLista>(Json))!.Id;
    }
}
