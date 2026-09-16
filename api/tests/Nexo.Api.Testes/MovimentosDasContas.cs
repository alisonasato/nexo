using System.Net;
using System.Net.Http.Json;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;
using static Nexo.Api.Testes.Apoio;

namespace Nexo.Api.Testes;

/// <summary>
/// O dinheiro chegando às contas: a baixa que vira movimento, o estorno que o
/// tira, o saldo que sai dos dois e a conta que recebe as cobranças.
///
/// <para>
/// O que se confere aqui é o que faz o saldo mentir sem erro nenhum: baixa que
/// não entra na conta, estorno que deixa o dinheiro lá, movimento antes do saldo
/// inicial contando o mesmo dinheiro duas vezes, e saldo inicial mudando por
/// baixo de movimentos que já existiam.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class MovimentosDasContas(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Baixa_a_receber_entra_e_a_pagar_sai_do_saldo_da_conta()
    {
        var http = await Entrar();
        var conta = await ContasBancariasDeTeste.Criar(http, saldo: 1_000m);

        var aReceber = await Lancar(http, NaturezaLancamento.Receber, 300m);
        var aPagar = await Lancar(http, NaturezaLancamento.Pagar, 200m);

        (await Baixar(http, aReceber, conta, 300m)).EnsureSuccessStatusCode();
        var baixa = await Baixar(http, aPagar, conta, 200m);
        baixa.EnsureSuccessStatusCode();

        var depois = await Conta(http, conta);
        Assert.Equal(1_100m, depois.SaldoAtual);
        Assert.True(depois.TemMovimentos);

        /* A baixa diz onde entrou, que é a primeira pergunta de quem concilia. */
        Assert.Equal(depois.Nome, (await baixa.Content.ReadFromJsonAsync<LancamentoNaLista>(Json))!.ContaDaBaixa);
    }

    [Fact]
    public async Task Estorno_tira_o_movimento_e_devolve_o_saldo()
    {
        var http = await Entrar();
        var conta = await ContasBancariasDeTeste.Criar(http, saldo: 1_000m);
        var lancamento = await Lancar(http, NaturezaLancamento.Receber, 300m);

        (await Baixar(http, lancamento, conta, 300m)).EnsureSuccessStatusCode();
        (await http.PostAsync($"/lancamentos/{lancamento}/estornar", null)).EnsureSuccessStatusCode();

        var depois = await Conta(http, conta);
        Assert.Equal(1_000m, depois.SaldoAtual);
        Assert.False(depois.TemMovimentos);
    }

    [Fact]
    public async Task Baixa_sem_conta_em_conta_inativa_ou_antes_do_saldo_inicial_e_recusada()
    {
        var http = await Entrar();
        var lancamento = await Lancar(http, NaturezaLancamento.Receber, 300m);

        var semConta = await http.PostAsJsonAsync($"/lancamentos/{lancamento}/baixar",
            new DadosDaBaixa(Guid.Empty, 300m, new DateOnly(2026, 3, 10)), Json);
        Assert.Equal("contaId", await CampoRecusado(semConta));

        var inativa = await ContasBancariasDeTeste.Criar(http);
        (await http.PutAsJsonAsync($"/contas-bancarias/{inativa}",
            Dados(await Conta(http, inativa)) with { Ativa = false }, Json)).EnsureSuccessStatusCode();
        Assert.Equal("contaId", await CampoRecusado(await Baixar(http, lancamento, inativa, 300m)));

        /*
         * A conta começa no primeiro dia de 2026. O dinheiro de antes está dentro
         * do saldo inicial, e um movimento anterior o contaria duas vezes.
         */
        var conta = await ContasBancariasDeTeste.Criar(http);
        var antes = await http.PostAsJsonAsync($"/lancamentos/{lancamento}/baixar",
            new DadosDaBaixa(conta, 300m, new DateOnly(2025, 12, 31)), Json);
        Assert.Equal("pagoEm", await CampoRecusado(antes));

        /* Nenhuma das recusas baixou nada, nem mexeu em saldo nenhum. */
        var lista = await http.GetFromJsonAsync<PaginaDeLancamentos>("/lancamentos", Json);
        Assert.Equal(SituacaoLancamento.Aberto, Assert.Single(lista!.Itens).Situacao);
        Assert.Equal(0m, (await Conta(http, conta)).SaldoAtual);
    }

    [Fact]
    public async Task Lote_leva_todos_para_a_mesma_conta_e_conta_invalida_nao_baixa_nenhum()
    {
        var http = await Entrar();
        var conta = await ContasBancariasDeTeste.Criar(http);
        var primeiro = await Lancar(http, NaturezaLancamento.Receber, 100m);
        var segundo = await Lancar(http, NaturezaLancamento.Receber, 250m);

        var recusado = await http.PostAsJsonAsync("/lancamentos/baixar-em-lote",
            new DadosDaBaixaEmLote(Guid.NewGuid(), [primeiro, segundo], new DateOnly(2026, 3, 10)), Json);
        Assert.Equal("contaId", await CampoRecusado(recusado));

        var lote = await http.PostAsJsonAsync("/lancamentos/baixar-em-lote",
            new DadosDaBaixaEmLote(conta, [primeiro, segundo], new DateOnly(2026, 3, 10)), Json);
        Assert.Equal(2, (await lote.Content.ReadFromJsonAsync<ResultadoDaBaixaEmLote>(Json))!.Baixados);

        Assert.Equal(350m, (await Conta(http, conta)).SaldoAtual);
    }

    [Fact]
    public async Task Saldo_inicial_nao_muda_depois_do_primeiro_movimento()
    {
        var http = await Entrar();
        var conta = await ContasBancariasDeTeste.Criar(http, saldo: 500m);
        var lancamento = await Lancar(http, NaturezaLancamento.Receber, 100m);
        (await Baixar(http, lancamento, conta, 100m)).EnsureSuccessStatusCode();

        var atual = await Conta(http, conta);

        var outroSaldo = await http.PutAsJsonAsync($"/contas-bancarias/{conta}",
            Dados(atual) with { SaldoInicial = 800m }, Json);
        Assert.Equal("saldoInicial", await CampoRecusado(outroSaldo));

        /* O resto da conta continua alterável: só o saldo inicial está por baixo dos movimentos. */
        var outroNome = await http.PutAsJsonAsync($"/contas-bancarias/{conta}",
            Dados(atual) with { Nome = "Renomeada" }, Json);
        Assert.Equal(HttpStatusCode.OK, outroNome.StatusCode);
        Assert.Equal(600m, (await Conta(http, conta)).SaldoAtual);
    }

    [Fact]
    public async Task Marcar_outra_conta_para_receber_as_cobrancas_tira_a_marca_da_primeira()
    {
        var http = await Entrar();
        await ContasBancariasDeTeste.Criar(http, recebeCobrancas: true);
        var segunda = await ContasBancariasDeTeste.Criar(http, recebeCobrancas: true);

        var marcadas = (await ListarContas(http))
            .Where(conta => conta.RecebeCobrancas)
            .Select(conta => conta.Id)
            .ToList();
        Assert.Equal([segunda], marcadas);

        /* E a conta que recebe não se inativa: o boleto já emitido ficaria sem destino. */
        var inativar = await http.PutAsJsonAsync($"/contas-bancarias/{segunda}",
            Dados(await Conta(http, segunda)) with { Ativa = false }, Json);
        Assert.Equal("recebeCobrancas", await CampoRecusado(inativar));
    }

    /* --------------------------------------------------------- apoio */

    private async Task<HttpClient> Entrar() =>
        await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

    /// <summary>Um lançamento avulso, com uma pessoa que tem o papel que a natureza pede.</summary>
    private static async Task<Guid> Lancar(HttpClient http, NaturezaLancamento natureza, decimal valor)
    {
        var papel = natureza == NaturezaLancamento.Pagar ? Papel.Fornecedor : Papel.Cliente;

        var pessoa = await http.PostAsJsonAsync("/pessoas", new DadosDePessoa(
            TipoPessoa.Juridica, RegimeTributario.SimplesNacional, string.Empty, [papel],
            "Pessoa do teste", string.Empty, Documentos.CnpjValido(), string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, null, string.Empty, true), Json);
        pessoa.EnsureSuccessStatusCode();
        var pessoaId = (await pessoa.Content.ReadFromJsonAsync<PessoaDetalhada>(Json))!.Id;

        var lancamento = await http.PostAsJsonAsync("/lancamentos", new DadosDoAvulso(
            natureza, pessoaId, "Lançamento do teste", valor, new DateOnly(2026, 3, 10), 2026, 3), Json);
        lancamento.EnsureSuccessStatusCode();

        return (await lancamento.Content.ReadFromJsonAsync<LancamentoNaLista>(Json))!.Id;
    }

    private static Task<HttpResponseMessage> Baixar(HttpClient http, Guid lancamento, Guid conta, decimal valor) =>
        http.PostAsJsonAsync($"/lancamentos/{lancamento}/baixar",
            new DadosDaBaixa(conta, valor, new DateOnly(2026, 3, 10)), Json);

    private static async Task<List<ContaNaLista>> ListarContas(HttpClient http) =>
        (await http.GetFromJsonAsync<List<ContaNaLista>>("/contas-bancarias", Json))!;

    private static async Task<ContaNaLista> Conta(HttpClient http, Guid id) =>
        (await ListarContas(http)).Single(conta => conta.Id == id);

    private static DadosDaConta Dados(ContaNaLista conta) => new(
        conta.Nome, conta.Tipo, conta.Banco, conta.Agencia, conta.Numero,
        conta.SaldoInicial, conta.SaldoInicialEm, conta.Ativa, conta.RecebeCobrancas);
}
