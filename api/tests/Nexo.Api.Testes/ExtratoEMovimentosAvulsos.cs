using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// O extrato das contas, os movimentos avulsos e as transferências.
///
/// <para>
/// O que se confere é o que faria o saldo daqui deixar de bater com o do banco:
/// tarifa entrando com o sinal errado, transferência com uma ponta só, fluxo de
/// caixa contando como receita o dinheiro que só mudou de conta, e movimento de
/// baixa apagado por baixo de um lançamento que continua pago.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class ExtratoEMovimentosAvulsos(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly DateOnly Hoje = DateOnly.FromDateTime(DateTime.Today);

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Tarifa_sai_e_rendimento_entra_com_o_saldo_linha_a_linha()
    {
        var http = await Entrar();
        var conta = await ContasBancariasDeTeste.Criar(http, saldo: 1_000m);

        await Avulso(http, conta, TipoDeMovimentoAvulso.Tarifa, 15m, Hoje.AddDays(-3));
        await Avulso(http, conta, TipoDeMovimentoAvulso.Rendimento, 4.5m, Hoje.AddDays(-1));

        var extrato = await Extrato(http, conta, Hoje.AddDays(-5), Hoje);

        Assert.Equal(1_000m, extrato.SaldoNoInicio);
        Assert.Equal([985m, 989.5m], extrato.Movimentos.Select(movimento => movimento.SaldoDepois).ToList());

        /* Sem descrição, a tarifa se explica sozinha no extrato. */
        Assert.Equal("Tarifa bancária", extrato.Movimentos[0].Descricao);

        Assert.Equal(989.5m, extrato.SaldoNoFim);
        Assert.Equal(989.5m, (await Conta(http, conta)).SaldoAtual);
    }

    [Fact]
    public async Task Transferencia_muda_o_dinheiro_de_conta_sem_mudar_o_total()
    {
        var http = await Entrar();
        var origem = await ContasBancariasDeTeste.Criar(http, saldo: 1_000m);
        var destino = await ContasBancariasDeTeste.Criar(http, saldo: 200m);

        var resposta = await http.PostAsJsonAsync("/contas-bancarias/transferencias",
            new DadosDaTransferencia(origem, destino, Hoje, 300m, null), Json);
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);

        Assert.Equal(700m, (await Conta(http, origem)).SaldoAtual);
        Assert.Equal(500m, (await Conta(http, destino)).SaldoAtual);

        /* Para o escritório nada entrou nem saiu: o fluxo de caixa não conta a transferência. */
        var fluxo = await http.GetFromJsonAsync<FluxoNoPeriodo>(
            $"/fluxo-de-caixa?de={Hoje:yyyy-MM-dd}&ate={Hoje:yyyy-MM-dd}", Json);

        Assert.Equal((0m, 0m), (fluxo!.TotalDeEntradas, fluxo.TotalDeSaidas));
        Assert.Equal(1_200m, fluxo.SaldoNoFim);
    }

    [Fact]
    public async Task Apagar_uma_ponta_da_transferencia_apaga_as_duas()
    {
        var http = await Entrar();
        var origem = await ContasBancariasDeTeste.Criar(http, saldo: 1_000m);
        var destino = await ContasBancariasDeTeste.Criar(http, saldo: 200m);

        (await http.PostAsJsonAsync("/contas-bancarias/transferencias",
            new DadosDaTransferencia(origem, destino, Hoje, 300m, "Reserva"), Json)).EnsureSuccessStatusCode();

        var ponta = Assert.Single((await Extrato(http, origem, Hoje, Hoje)).Movimentos);
        Assert.True(ponta.PodeApagar);

        var resposta = await http.DeleteAsync($"/contas-bancarias/{origem}/movimentos/{ponta.Id}");
        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);

        Assert.Equal(1_000m, (await Conta(http, origem)).SaldoAtual);
        Assert.Equal(200m, (await Conta(http, destino)).SaldoAtual);
        Assert.Empty((await Extrato(http, destino, Hoje, Hoje)).Movimentos);
    }

    [Fact]
    public async Task Movimento_de_baixa_nao_se_apaga_pelo_extrato()
    {
        var http = await Entrar();
        var conta = await ContasBancariasDeTeste.Criar(http);
        await LancarEBaixar(http, conta, 250m);

        var daBaixa = Assert.Single((await Extrato(http, conta, Hoje, Hoje)).Movimentos);
        Assert.False(daBaixa.PodeApagar);

        /*
         * Apagá-lo deixaria o lançamento pago sem dinheiro nenhum entrando. Quem
         * o desfaz é o estorno, que reabre o lançamento junto.
         */
        var resposta = await http.DeleteAsync($"/contas-bancarias/{conta}/movimentos/{daBaixa.Id}");
        Assert.Equal("movimentoId", await CampoRecusado(resposta));
        Assert.Equal(250m, (await Conta(http, conta)).SaldoAtual);
    }

    [Fact]
    public async Task Movimento_e_transferencia_invalidos_sao_recusados()
    {
        var http = await Entrar();
        var conta = await ContasBancariasDeTeste.Criar(http);

        var semDescricao = await http.PostAsJsonAsync($"/contas-bancarias/{conta}/movimentos",
            new DadosDoMovimentoAvulso(TipoDeMovimentoAvulso.OutraSaida, Hoje, 50m, " "), Json);
        Assert.Equal("descricao", await CampoRecusado(semDescricao));

        /* A conta começa no primeiro dia de 2026: o dinheiro de antes já está no saldo inicial. */
        var antesDoSaldo = await http.PostAsJsonAsync($"/contas-bancarias/{conta}/movimentos",
            new DadosDoMovimentoAvulso(TipoDeMovimentoAvulso.Tarifa, new DateOnly(2025, 12, 31), 10m, null), Json);
        Assert.Equal("data", await CampoRecusado(antesDoSaldo));

        var mesmaConta = await http.PostAsJsonAsync("/contas-bancarias/transferencias",
            new DadosDaTransferencia(conta, conta, Hoje, 10m, null), Json);
        Assert.Equal("destinoId", await CampoRecusado(mesmaConta));

        Assert.Empty((await Extrato(http, conta, Hoje, Hoje)).Movimentos);
    }

    /* --------------------------------------------------------- apoio */

    private async Task<HttpClient> Entrar() =>
        await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

    private static async Task Avulso(
        HttpClient http, Guid conta, TipoDeMovimentoAvulso tipo, decimal valor, DateOnly data)
    {
        var resposta = await http.PostAsJsonAsync($"/contas-bancarias/{conta}/movimentos",
            new DadosDoMovimentoAvulso(tipo, data, valor, null), Json);
        resposta.EnsureSuccessStatusCode();
    }

    private static async Task<ExtratoDaConta> Extrato(HttpClient http, Guid conta, DateOnly de, DateOnly ate) =>
        (await http.GetFromJsonAsync<ExtratoDaConta>(
            $"/contas-bancarias/{conta}/extrato?de={de:yyyy-MM-dd}&ate={ate:yyyy-MM-dd}", Json))!;

    private static async Task<ContaNaLista> Conta(HttpClient http, Guid id) =>
        (await http.GetFromJsonAsync<List<ContaNaLista>>("/contas-bancarias", Json))!.Single(conta => conta.Id == id);

    private static async Task LancarEBaixar(HttpClient http, Guid conta, decimal valor)
    {
        var pessoa = await http.PostAsJsonAsync("/pessoas", new DadosDePessoa(
            TipoPessoa.Juridica, RegimeTributario.SimplesNacional, string.Empty, [Papel.Cliente],
            "Cliente do teste", string.Empty, Documentos.CnpjValido(), string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, null, string.Empty, true), Json);
        pessoa.EnsureSuccessStatusCode();
        var pessoaId = (await pessoa.Content.ReadFromJsonAsync<PessoaDetalhada>(Json))!.Id;

        var lancamento = await http.PostAsJsonAsync("/lancamentos", new DadosDoAvulso(
            NaturezaLancamento.Receber, pessoaId, "Honorários", valor, Hoje, Hoje.Year, Hoje.Month), Json);
        lancamento.EnsureSuccessStatusCode();
        var lancamentoId = (await lancamento.Content.ReadFromJsonAsync<LancamentoNaLista>(Json))!.Id;

        var baixa = await http.PostAsJsonAsync($"/lancamentos/{lancamentoId}/baixar",
            new DadosDaBaixa(conta, valor, Hoje), Json);
        baixa.EnsureSuccessStatusCode();
    }

    private static async Task<string> CampoRecusado(HttpResponseMessage resposta)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>(Json);
        return Assert.Single(corpo!.Problemas).Campo;
    }
}
