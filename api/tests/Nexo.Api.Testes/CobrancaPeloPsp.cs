using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Api.Cobranca;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;
using static Nexo.Api.Testes.Apoio;

namespace Nexo.Api.Testes;

/// <summary>
/// A cobrança pelo PSP e a baixa que volta dele.
///
/// <para>
/// É o segundo teste que a decisão Q30 declarou obrigatório — o primeiro é o
/// vazamento entre tenants. O motivo é o mesmo dos dois: aqui o erro custa
/// dinheiro de verdade, e custa do jeito que não se conserta com pedido de
/// desculpas. Cobrar duas vezes é o cliente pagando em dobro; baixar duas vezes
/// é o escritório achando que recebeu o que não recebeu.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class CobrancaPeloPsp : IDisposable
{
    private const string TokenDoWebhook = "token-combinado-com-o-psp";

    private readonly BancoDeTestes _banco;
    private readonly AsaasDeMentira _psp = new();
    private readonly AplicacaoDeTestes _aplicacao;

    public CobrancaPeloPsp(BancoDeTestes banco)
    {
        _banco = banco;

        _aplicacao = new AplicacaoDeTestes(banco.Conexao, servicos =>
        {
            servicos.Configure<OpcoesDoAsaas>(opcoes =>
            {
                opcoes.Chave = "chave-de-mentira";
                opcoes.TokenDoWebhook = TokenDoWebhook;
            });

            servicos.AddHttpClient<ClienteDoAsaas>()
                .ConfigurePrimaryHttpMessageHandler(() => _psp);
        });
    }

    public void Dispose() => _aplicacao.Dispose();

    /* ------------------------------------------------------------ emitir */

    [Fact]
    public async Task Cobrar_emite_no_psp_e_guarda_o_link()
    {
        var (http, conta) = await Entrar();
        var lancamento = await CriarLancamento(http);

        var resposta = await http.PostAsync($"/lancamentos/{lancamento}/cobrar", null);
        resposta.EnsureSuccessStatusCode();

        var cobrado = await resposta.Content.ReadFromJsonAsync<LancamentoCobrado>(Json);
        Assert.Equal("pay_000001", cobrado!.CobrancaId);
        Assert.Equal("https://sandbox.asaas.com/i/pay_000001", cobrado.CobrancaUrl);

        /*
         * A referência leva o tenant junto do lançamento. É por ela que o
         * webhook — que chega sem sessão — descobre de quem é a linha, sem
         * precisar de um caminho que ignore o isolamento.
         */
        var referencia = _psp.UltimaCobranca.GetProperty("externalReference").GetString();
        Assert.Equal($"{conta.TenantId}/{lancamento}", referencia);

        /* E a forma de pagamento fica em aberto: quem escolhe é quem paga. */
        Assert.Equal("UNDEFINED", _psp.UltimaCobranca.GetProperty("billingType").GetString());
    }

    [Fact]
    public async Task Cobrar_duas_vezes_nao_emite_duas_cobrancas()
    {
        var (http, _) = await Entrar();
        var lancamento = await CriarLancamento(http);

        var primeira = await http.PostAsync($"/lancamentos/{lancamento}/cobrar", null);
        var segunda = await http.PostAsync($"/lancamentos/{lancamento}/cobrar", null);

        primeira.EnsureSuccessStatusCode();
        segunda.EnsureSuccessStatusCode();

        /*
         * Dois cliques no mesmo botão é o caso comum. A segunda emissão não
         * substituiria a primeira: criaria outra cobrança do mesmo valor, e o
         * cliente receberia dois boletos.
         */
        Assert.Equal(1, _psp.CobrancasCriadas);

        var a = await primeira.Content.ReadFromJsonAsync<LancamentoCobrado>(Json);
        var b = await segunda.Content.ReadFromJsonAsync<LancamentoCobrado>(Json);
        Assert.Equal(a!.CobrancaId, b!.CobrancaId);
    }

    [Fact]
    public async Task Cliente_sem_documento_e_recusado_antes_de_falar_com_o_psp()
    {
        var (http, conta) = await Entrar();
        var lancamento = await CriarLancamento(http);

        /*
         * O documento é apagado direto no banco porque o cadastro não deixa
         * gravar pessoa sem ele. A conferência na cobrança não é redundância:
         * ela defende a fronteira de um campo que pertence a outra tela, e
         * quem garante que ele está lá é uma regra que pode mudar. Sem ela, a
         * falta vira "cpfCnpj inválido" vindo do PSP — verdade que não diz
         * onde consertar.
         */
        await using (var contexto = _banco.Criar(conta.TenantId))
        {
            await contexto.Pessoas.ExecuteUpdateAsync(
                ajuste => ajuste.SetProperty(p => p.Documento, string.Empty));
        }

        var resposta = await http.PostAsync($"/lancamentos/{lancamento}/cobrar", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);

        /*
         * Nada saiu. O PSP recusaria também, com "cpfCnpj inválido" — verdade
         * que não ajuda quem está olhando a lista de lançamentos e precisa saber
         * que o conserto é no cadastro daqui.
         */
        Assert.Equal(0, _psp.ClientesCriados);
        Assert.Equal(0, _psp.CobrancasCriadas);
    }

    [Fact]
    public async Task O_mesmo_cliente_nao_vira_dois_cadastros_no_psp()
    {
        var (http, _) = await Entrar();

        var pessoa = await CriarPessoa(http, Documentos.CnpjValido());
        var janeiro = await CriarAvulso(http, pessoa, 2026, 1);
        var fevereiro = await CriarAvulso(http, pessoa, 2026, 2);

        await http.PostAsync($"/lancamentos/{janeiro}/cobrar", null);
        await http.PostAsync($"/lancamentos/{fevereiro}/cobrar", null);

        /*
         * Sem guardar o identificador do pagador, cada mensalidade criaria um
         * cadastro novo lá — o mesmo cliente repetido uma vez por mês, com o
         * histórico dele espalhado por dezenas de cadastros.
         */
        Assert.Equal(1, _psp.ClientesCriados);
        Assert.Equal(2, _psp.CobrancasCriadas);
    }

    [Fact]
    public async Task Recusa_do_psp_vira_mensagem_e_nao_erro_generico()
    {
        var (http, _) = await Entrar();
        var lancamento = await CriarLancamento(http);

        _psp.Recusa = "O valor da cobrança deve ser maior que zero.";

        var resposta = await http.PostAsync($"/lancamentos/{lancamento}/cobrar", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);

        var problemas = await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>(Json);
        Assert.Contains("maior que zero", problemas!.Problemas[0].Descricao);
    }

    /* ---------------------------------------------------------- webhook */

    [Fact]
    public async Task Sem_o_token_combinado_o_aviso_nao_e_processado()
    {
        var (http, conta) = await Entrar();
        var lancamento = await CriarLancamento(http);
        await http.PostAsync($"/lancamentos/{lancamento}/cobrar", null);

        var anonimo = _aplicacao.CreateClient();
        var resposta = await anonimo.PostAsJsonAsync("/integracoes/asaas/webhook",
            Aviso("evt_1", "PAYMENT_RECEIVED", conta.TenantId, lancamento, 450m));

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal(SituacaoLancamento.Aberto, await SituacaoDe(conta.TenantId, lancamento));
    }

    [Fact]
    public async Task Pagamento_confirmado_da_baixa_com_origem_de_cobranca()
    {
        var (http, conta) = await Entrar();
        var lancamento = await CriarLancamento(http);
        await http.PostAsync($"/lancamentos/{lancamento}/cobrar", null);

        var resposta = await Avisar(Aviso("evt_1", "PAYMENT_CONFIRMED", conta.TenantId, lancamento, 450m));
        resposta.EnsureSuccessStatusCode();

        var baixado = await Buscar(conta.TenantId, lancamento);
        Assert.Equal(SituacaoLancamento.Pago, baixado.Situacao);
        Assert.Equal(450m, baixado.ValorPago);
        Assert.Equal(new DateOnly(2026, 3, 8), baixado.PagoEm);

        /*
         * A origem é o que responde "quem deu essa baixa" quando o valor não
         * bate. Sem ela, uma baixa automática errada e uma baixa manual errada
         * são indistinguíveis seis meses depois.
         */
        Assert.Equal(OrigensDeBaixa.Cobranca, baixado.OrigemDaBaixa);
    }

    [Fact]
    public async Task O_reenvio_de_um_aviso_antigo_nao_desfaz_o_que_veio_depois()
    {
        var (http, conta) = await Entrar();
        var lancamento = await CriarLancamento(http);
        await http.PostAsync($"/lancamentos/{lancamento}/cobrar", null);

        /* O cliente paga. */
        await Avisar(Aviso("evt_pago", "PAYMENT_RECEIVED", conta.TenantId, lancamento, 450m));
        Assert.Equal(SituacaoLancamento.Pago, await SituacaoDe(conta.TenantId, lancamento));

        /* Depois o pagamento é estornado, e o valor volta a ser devido. */
        await Avisar(Aviso("evt_estorno", "PAYMENT_REFUNDED", conta.TenantId, lancamento, 450m));
        Assert.Equal(SituacaoLancamento.Aberto, await SituacaoDe(conta.TenantId, lancamento));

        /*
         * E então o aviso do pagamento chega de novo.
         *
         * Não é caso inventado: a entrega do Asaas é at least once, ele
         * reenvia, e guarda evento por 14 dias. Sem a tabela de eventos, este
         * reenvio daria baixa num lançamento que foi legitimamente reaberto — e
         * o escritório pararia de cobrar alguém que deve.
         */
        await Avisar(Aviso("evt_pago", "PAYMENT_RECEIVED", conta.TenantId, lancamento, 450m));
        Assert.Equal(SituacaoLancamento.Aberto, await SituacaoDe(conta.TenantId, lancamento));
    }

    [Fact]
    public async Task Estorno_reabre_em_vez_de_cancelar()
    {
        var (http, conta) = await Entrar();
        var lancamento = await CriarLancamento(http);
        await http.PostAsync($"/lancamentos/{lancamento}/cobrar", null);

        await Avisar(Aviso("evt_1", "PAYMENT_RECEIVED", conta.TenantId, lancamento, 450m));
        await Avisar(Aviso("evt_2", "PAYMENT_REFUNDED", conta.TenantId, lancamento, 450m));

        var reaberto = await Buscar(conta.TenantId, lancamento);

        /*
         * Cancelar apagaria a cobrança da conta do escritório, que é o
         * contrário do que estorno significa: o dinheiro voltou, e o valor
         * continua devido.
         */
        Assert.Equal(SituacaoLancamento.Aberto, reaberto.Situacao);
        Assert.Null(reaberto.ValorPago);
        Assert.Null(reaberto.PagoEm);
    }

    [Fact]
    public async Task Um_tenant_nao_baixa_o_lancamento_do_outro()
    {
        var (http, dono) = await Entrar();
        var lancamento = await CriarLancamento(http);
        await http.PostAsync($"/lancamentos/{lancamento}/cobrar", null);

        var intruso = await Contas.Criar(_banco, _aplicacao);

        /*
         * Um aviso que nomeia o tenant errado para o lançamento certo. O token
         * confere — é o mesmo — mas a política de linha não devolve a linha, e
         * não há nada a baixar. Quem protege aqui é o Postgres, e não um
         * `where` que alguém possa esquecer.
         */
        var resposta = await Avisar(Aviso("evt_1", "PAYMENT_RECEIVED", intruso.TenantId, lancamento, 450m));
        resposta.EnsureSuccessStatusCode();

        Assert.Equal(SituacaoLancamento.Aberto, await SituacaoDe(dono.TenantId, lancamento));
    }

    [Fact]
    public async Task Aviso_que_nao_interessa_responde_200()
    {
        var (http, conta) = await Entrar();
        var lancamento = await CriarLancamento(http);
        await http.PostAsync($"/lancamentos/{lancamento}/cobrar", null);

        /*
         * Responder erro aqui pararia a fila do webhook depois de 15 avisos
         * seguidos que não entendemos — e com ela os avisos de pagamento, que
         * são os que importam. Eventos parados somem em 14 dias.
         */
        var resposta = await Avisar(
            Aviso("evt_1", "PAYMENT_BANK_SLIP_VIEWED", conta.TenantId, lancamento, 450m));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(SituacaoLancamento.Aberto, await SituacaoDe(conta.TenantId, lancamento));
    }

    /* ------------------------------------------- tirar a cobrança do ar */

    [Fact]
    public async Task Cancelar_tira_a_cobranca_do_psp()
    {
        var (http, conta) = await Entrar();
        var lancamento = await CriarLancamento(http);
        await http.PostAsync($"/lancamentos/{lancamento}/cobrar", null);

        var resposta = await http.PostAsJsonAsync($"/lancamentos/{lancamento}/cancelar",
            new DadosDoCancelamento("Cliente desistiu do serviço"), Json);
        resposta.EnsureSuccessStatusCode();

        /*
         * Antes disto, o boleto e o Pix continuavam pagáveis depois do
         * cancelamento. O cliente pagava, e o aviso chegava para um lançamento
         * que não esperava mais por ele.
         */
        Assert.Equal(["pay_000001"], _psp.CobrancasExcluidas);

        var cancelado = await Buscar(conta.TenantId, lancamento);
        Assert.Equal(SituacaoLancamento.Cancelado, cancelado.Situacao);
        Assert.Equal(string.Empty, cancelado.CobrancaId);
    }

    [Fact]
    public async Task Baixa_manual_tira_a_cobranca_do_psp()
    {
        var (http, conta) = await Entrar();
        var lancamento = await CriarLancamento(http);
        await http.PostAsync($"/lancamentos/{lancamento}/cobrar", null);

        var resposta = await http.PostAsJsonAsync($"/lancamentos/{lancamento}/baixar",
            new DadosDaBaixa(await ContasBancariasDeTeste.Criar(http), 450m, new DateOnly(2026, 3, 8)), Json);
        resposta.EnsureSuccessStatusCode();

        /* Quem baixa à mão diz que o dinheiro entrou por fora. A cobrança que
           ficasse no ar seria um segundo pagamento esperando acontecer. */
        Assert.Equal(["pay_000001"], _psp.CobrancasExcluidas);

        var baixado = await Buscar(conta.TenantId, lancamento);
        Assert.Equal(SituacaoLancamento.Pago, baixado.Situacao);
        Assert.Equal(OrigensDeBaixa.Manual, baixado.OrigemDaBaixa);
    }

    [Fact]
    public async Task Se_o_psp_nao_retira_a_cobranca_o_cancelamento_nao_acontece()
    {
        var (http, conta) = await Entrar();
        var lancamento = await CriarLancamento(http);
        await http.PostAsync($"/lancamentos/{lancamento}/cobrar", null);

        /* O cliente acabou de pagar, e o aviso ainda não chegou. */
        _psp.RecusaAoExcluir = "A cobrança já foi recebida.";

        var resposta = await http.PostAsJsonAsync($"/lancamentos/{lancamento}/cancelar",
            new DadosDoCancelamento("Cliente desistiu do serviço"), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);

        /*
         * Nada mudou aqui. Cancelar mesmo assim faria a baixa, que está a
         * caminho, chegar para um lançamento cancelado.
         */
        var intacto = await Buscar(conta.TenantId, lancamento);
        Assert.Equal(SituacaoLancamento.Aberto, intacto.Situacao);
        Assert.Equal("pay_000001", intacto.CobrancaId);
    }

    [Fact]
    public async Task Pagamento_que_chega_para_lancamento_cancelado_fica_marcado_para_conferir()
    {
        var (http, conta) = await Entrar();
        var lancamento = await CriarLancamento(http);
        await http.PostAsync($"/lancamentos/{lancamento}/cobrar", null);

        var cancelamento = await http.PostAsJsonAsync($"/lancamentos/{lancamento}/cancelar",
            new DadosDoCancelamento("Cliente desistiu do serviço"), Json);
        cancelamento.EnsureSuccessStatusCode();

        /*
         * Excluir no PSP não desfaz um pagamento que já estava em curso, e o
         * aviso dele pode chegar depois do cancelamento.
         */
        var resposta = await Avisar(Aviso("evt_tardio", "PAYMENT_RECEIVED", conta.TenantId, lancamento, 450m));
        resposta.EnsureSuccessStatusCode();

        Assert.Equal(SituacaoLancamento.Cancelado, await SituacaoDe(conta.TenantId, lancamento));

        /*
         * O dinheiro entrou, e isso não pode passar calado. Antes, este aviso
         * era descartado sem deixar rastro diferente de um boleto visualizado.
         */
        await using var contexto = _banco.Criar(conta.TenantId);
        var evento = await contexto.EventosDeCobranca.AsNoTracking()
            .SingleAsync(e => e.Id == "evt_tardio");

        Assert.Contains("cancelado", evento.Divergencia);
    }

    [Fact]
    public async Task Sem_integracao_configurada_cancelar_segue_e_guarda_a_referencia()
    {
        var (http, conta) = await Entrar();
        var lancamento = await CriarLancamento(http);

        /* Uma cobrança emitida antes de a integração ser desligada. */
        await using (var contexto = _banco.Criar(conta.TenantId))
        {
            await contexto.Lancamentos.Where(r => r.Id == lancamento).ExecuteUpdateAsync(ajuste => ajuste
                .SetProperty(r => r.CobrancaId, "pay_antiga")
                .SetProperty(r => r.CobrancaUrl, "https://sandbox.asaas.com/i/pay_antiga"));
        }

        using var semIntegracao = new AplicacaoDeTestes(_banco.Conexao);
        var outroHttp = await Contas.Entrar(semIntegracao, conta);

        var resposta = await outroHttp.PostAsJsonAsync($"/lancamentos/{lancamento}/cancelar",
            new DadosDoCancelamento("Serviço não prestado"), Json);
        resposta.EnsureSuccessStatusCode();

        var cancelado = await Buscar(conta.TenantId, lancamento);
        Assert.Equal(SituacaoLancamento.Cancelado, cancelado.Situacao);

        /*
         * Travar o cancelamento de tudo o que foi cobrado antes seria punir
         * quem desligou a integração. A referência fica, porque é por ela que
         * alguém acha a cobrança no painel do PSP para tirá-la de lá à mão.
         */
        Assert.Empty(_psp.CobrancasExcluidas);
        Assert.Equal("pay_antiga", cancelado.CobrancaId);
    }

    [Fact]
    public async Task Duas_gravacoes_sobre_o_mesmo_lancamento_nao_passam_uma_por_cima_da_outra()
    {
        var (http, conta) = await Entrar();
        var lancamento = await CriarLancamento(http);

        /*
         * Dois leitores veem "em aberto" ao mesmo tempo: o aviso do PSP e
         * alguém cancelando noutra aba. Sem trava, o segundo a gravar passaria
         * por cima do primeiro, e a baixa do PSP sumiria debaixo de um
         * cancelamento.
         */
        await using var primeiro = _banco.Criar(conta.TenantId);
        await using var segundo = _banco.Criar(conta.TenantId);

        var peloPsp = await primeiro.Lancamentos.SingleAsync(r => r.Id == lancamento);
        var naOutraAba = await segundo.Lancamentos.SingleAsync(r => r.Id == lancamento);

        peloPsp.Situacao = SituacaoLancamento.Pago;
        peloPsp.ValorPago = 450m;
        peloPsp.PagoEm = new DateOnly(2026, 3, 8);
        peloPsp.OrigemDaBaixa = OrigensDeBaixa.Cobranca;
        await primeiro.SaveChangesAsync();

        naOutraAba.Situacao = SituacaoLancamento.Cancelado;
        naOutraAba.MotivoDoCancelamento = "Engano";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => segundo.SaveChangesAsync());
        Assert.Equal(SituacaoLancamento.Pago, await SituacaoDe(conta.TenantId, lancamento));
    }

    /* ----------------------------------------------- conta das cobranças */

    [Fact]
    public async Task Cobrar_sem_conta_que_receba_as_cobrancas_e_recusado()
    {
        var conta = await Contas.Criar(_banco, _aplicacao);
        var http = await Contas.Entrar(_aplicacao, conta);
        var lancamento = await CriarLancamento(http);

        var resposta = await http.PostAsync($"/lancamentos/{lancamento}/cobrar", null);

        /* Um boleto emitido sem conta para receber nasceria sem destino: o pagamento viraria divergência. */
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal(0, _psp.CobrancasCriadas);
    }

    [Fact]
    public async Task Pagamento_pelo_psp_entra_na_conta_das_cobrancas_e_o_estorno_compensa()
    {
        var (http, conta) = await Entrar();
        var lancamento = await CriarLancamento(http);
        await http.PostAsync($"/lancamentos/{lancamento}/cobrar", null);

        await Avisar(Aviso("evt_1", "PAYMENT_RECEIVED", conta.TenantId, lancamento, 450m));
        Assert.Equal(450m, (await ContaDasCobrancas(http)).SaldoAtual);

        await Avisar(Aviso("evt_2", "PAYMENT_REFUNDED", conta.TenantId, lancamento, 450m));
        Assert.Equal(0m, (await ContaDasCobrancas(http)).SaldoAtual);

        /*
         * O dinheiro entrou e voltou, e o extrato do PSP mostra as duas linhas.
         * Aqui também: a entrada fica, estornada, e a devolução ao lado.
         */
        await using var contexto = _banco.Criar(conta.TenantId);
        var origens = await contexto.MovimentosDeConta.AsNoTracking()
            .Where(movimento => movimento.LancamentoId == lancamento)
            .Select(movimento => movimento.Origem)
            .OrderBy(origem => origem)
            .ToListAsync();

        Assert.Equal([OrigensDeMovimento.BaixaEstornada, OrigensDeMovimento.EstornoDoPsp], origens);
    }

    [Fact]
    public async Task Sem_conta_marcada_o_pagamento_vira_divergencia_em_vez_de_baixa()
    {
        var (http, conta) = await Entrar();
        var lancamento = await CriarLancamento(http);
        await http.PostAsync($"/lancamentos/{lancamento}/cobrar", null);

        /* Alguém tira a marca da conta depois de a cobrança ter saído. */
        await using (var contexto = _banco.Criar(conta.TenantId))
        {
            await contexto.ContasBancarias.ExecuteUpdateAsync(
                ajuste => ajuste.SetProperty(c => c.RecebeCobrancas, false));
        }

        var resposta = await Avisar(Aviso("evt_sem_conta", "PAYMENT_RECEIVED", conta.TenantId, lancamento, 450m));
        resposta.EnsureSuccessStatusCode();

        Assert.Equal(SituacaoLancamento.Aberto, await SituacaoDe(conta.TenantId, lancamento));

        await using var leitura = _banco.Criar(conta.TenantId);
        var evento = await leitura.EventosDeCobranca.AsNoTracking().SingleAsync(e => e.Id == "evt_sem_conta");
        Assert.Contains("conta marcada", evento.Divergencia);
    }

    /* ---------------------------------------------------------- trilha */

    [Fact]
    public async Task A_baixa_do_aviso_fica_no_historico_em_nome_do_psp()
    {
        var (http, conta) = await Entrar();
        var lancamento = await CriarLancamento(http);
        (await http.PostAsync($"/lancamentos/{lancamento}/cobrar", null)).EnsureSuccessStatusCode();

        await Avisar(Aviso("evt_trilha", "PAYMENT_RECEIVED", conta.TenantId, lancamento, 450m));

        var historico = (await http.GetFromJsonAsync<List<EventoNoHistorico>>(
            $"/lancamentos/{lancamento}/historico", Json))!;

        /* Quem emitiu a cobrança foi a pessoa; quem deu a baixa foi o aviso, e não ela. */
        Assert.Equal("Pessoa de teste", Assert.Single(historico, evento => evento.Resumo == "Cobrança emitida").Autor);
        Assert.Equal("Asaas", Assert.Single(historico, evento => evento.Resumo == "Baixa").Autor);
    }

    /* --------------------------------------------------------- apoio */

    /// <summary>Entra já com uma conta bancária marcada para receber as cobranças, sem a qual não se cobra.</summary>
    private async Task<(HttpClient, ContaDeTestes)> Entrar()
    {
        var conta = await Contas.Criar(_banco, _aplicacao);
        var http = await Contas.Entrar(_aplicacao, conta);
        await ContasBancariasDeTeste.Criar(http, recebeCobrancas: true);
        return (http, conta);
    }

    private async Task<HttpResponseMessage> Avisar(object aviso)
    {
        var cliente = _aplicacao.CreateClient();
        cliente.DefaultRequestHeaders.Add("asaas-access-token", TokenDoWebhook);
        return await cliente.PostAsJsonAsync("/integracoes/asaas/webhook", aviso);
    }

    private static object Aviso(
        string id, string evento, Guid tenant, Guid lancamento, decimal valor) => new
        {
            id,
            @event = evento,
            payment = new
            {
                id = "pay_000001",
                externalReference = $"{tenant}/{lancamento}",
                value = valor,
                status = evento == "PAYMENT_REFUNDED" ? "REFUNDED" : "RECEIVED",
                clientPaymentDate = "2026-03-08",
            },
        };

    private async Task<Lancamento> Buscar(Guid tenant, Guid id)
    {
        await using var contexto = _banco.Criar(tenant);
        return await contexto.Lancamentos.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private async Task<SituacaoLancamento> SituacaoDe(Guid tenant, Guid id) =>
        (await Buscar(tenant, id)).Situacao;

    private static async Task<ContaNaLista> ContaDasCobrancas(HttpClient http) =>
        (await http.GetFromJsonAsync<List<ContaNaLista>>("/contas-bancarias", Json))!
            .Single(conta => conta.RecebeCobrancas);

    private static async Task<Guid> CriarPessoa(HttpClient http, string documento)
    {
        var resposta = await http.PostAsJsonAsync("/pessoas", new DadosDePessoa(
            TipoPessoa.Juridica, RegimeTributario.SimplesNacional, string.Empty, [Papel.Cliente],
            "Cliente do teste", string.Empty, documento, string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, null, string.Empty, true), Json);

        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<PessoaDetalhada>(Json))!.Id;
    }

    private static async Task<Guid> CriarAvulso(HttpClient http, Guid pessoa, int ano, int mes)
    {
        var resposta = await http.PostAsJsonAsync("/lancamentos", new DadosDoAvulso(NaturezaLancamento.Receber,
            pessoa, "Honorários", 450m, new DateOnly(ano, mes, 10), ano, mes), Json);

        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<LancamentoNaLista>(Json))!.Id;
    }

    private static async Task<Guid> CriarLancamento(HttpClient http)
    {
        var pessoa = await CriarPessoa(http, Documentos.CnpjValido());
        return await CriarAvulso(http, pessoa, 2026, 3);
    }
}
