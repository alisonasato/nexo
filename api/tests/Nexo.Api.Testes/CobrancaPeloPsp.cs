using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Api.Cobranca;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

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

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

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
        var recebivel = await CriarRecebivel(http);

        var resposta = await http.PostAsync($"/recebiveis/{recebivel}/cobrar", null);
        resposta.EnsureSuccessStatusCode();

        var cobrado = await resposta.Content.ReadFromJsonAsync<RecebivelCobrado>(Json);
        Assert.Equal("pay_000001", cobrado!.CobrancaId);
        Assert.Equal("https://sandbox.asaas.com/i/pay_000001", cobrado.CobrancaUrl);

        /*
         * A referência leva o tenant junto do recebível. É por ela que o
         * webhook — que chega sem sessão — descobre de quem é a linha, sem
         * precisar de um caminho que ignore o isolamento.
         */
        var referencia = _psp.UltimaCobranca.GetProperty("externalReference").GetString();
        Assert.Equal($"{conta.TenantId}/{recebivel}", referencia);

        /* E a forma de pagamento fica em aberto: quem escolhe é quem paga. */
        Assert.Equal("UNDEFINED", _psp.UltimaCobranca.GetProperty("billingType").GetString());
    }

    [Fact]
    public async Task Cobrar_duas_vezes_nao_emite_duas_cobrancas()
    {
        var (http, _) = await Entrar();
        var recebivel = await CriarRecebivel(http);

        var primeira = await http.PostAsync($"/recebiveis/{recebivel}/cobrar", null);
        var segunda = await http.PostAsync($"/recebiveis/{recebivel}/cobrar", null);

        primeira.EnsureSuccessStatusCode();
        segunda.EnsureSuccessStatusCode();

        /*
         * Dois cliques no mesmo botão é o caso comum. A segunda emissão não
         * substituiria a primeira: criaria outra cobrança do mesmo valor, e o
         * cliente receberia dois boletos.
         */
        Assert.Equal(1, _psp.CobrancasCriadas);

        var a = await primeira.Content.ReadFromJsonAsync<RecebivelCobrado>(Json);
        var b = await segunda.Content.ReadFromJsonAsync<RecebivelCobrado>(Json);
        Assert.Equal(a!.CobrancaId, b!.CobrancaId);
    }

    [Fact]
    public async Task Cliente_sem_documento_e_recusado_antes_de_falar_com_o_psp()
    {
        var (http, conta) = await Entrar();
        var recebivel = await CriarRecebivel(http);

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

        var resposta = await http.PostAsync($"/recebiveis/{recebivel}/cobrar", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);

        /*
         * Nada saiu. O PSP recusaria também, com "cpfCnpj inválido" — verdade
         * que não ajuda quem está olhando a lista de recebíveis e precisa saber
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

        await http.PostAsync($"/recebiveis/{janeiro}/cobrar", null);
        await http.PostAsync($"/recebiveis/{fevereiro}/cobrar", null);

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
        var recebivel = await CriarRecebivel(http);

        _psp.Recusa = "O valor da cobrança deve ser maior que zero.";

        var resposta = await http.PostAsync($"/recebiveis/{recebivel}/cobrar", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);

        var problemas = await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>(Json);
        Assert.Contains("maior que zero", problemas!.Problemas[0].Descricao);
    }

    /* ---------------------------------------------------------- webhook */

    [Fact]
    public async Task Sem_o_token_combinado_o_aviso_nao_e_processado()
    {
        var (http, conta) = await Entrar();
        var recebivel = await CriarRecebivel(http);
        await http.PostAsync($"/recebiveis/{recebivel}/cobrar", null);

        var anonimo = _aplicacao.CreateClient();
        var resposta = await anonimo.PostAsJsonAsync("/integracoes/asaas/webhook",
            Aviso("evt_1", "PAYMENT_RECEIVED", conta.TenantId, recebivel, 450m));

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal(SituacaoRecebivel.Aberto, await SituacaoDe(conta.TenantId, recebivel));
    }

    [Fact]
    public async Task Pagamento_confirmado_da_baixa_com_origem_de_cobranca()
    {
        var (http, conta) = await Entrar();
        var recebivel = await CriarRecebivel(http);
        await http.PostAsync($"/recebiveis/{recebivel}/cobrar", null);

        var resposta = await Avisar(Aviso("evt_1", "PAYMENT_CONFIRMED", conta.TenantId, recebivel, 450m));
        resposta.EnsureSuccessStatusCode();

        var baixado = await Buscar(conta.TenantId, recebivel);
        Assert.Equal(SituacaoRecebivel.Pago, baixado.Situacao);
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
        var recebivel = await CriarRecebivel(http);
        await http.PostAsync($"/recebiveis/{recebivel}/cobrar", null);

        /* O cliente paga. */
        await Avisar(Aviso("evt_pago", "PAYMENT_RECEIVED", conta.TenantId, recebivel, 450m));
        Assert.Equal(SituacaoRecebivel.Pago, await SituacaoDe(conta.TenantId, recebivel));

        /* Depois o pagamento é estornado, e o valor volta a ser devido. */
        await Avisar(Aviso("evt_estorno", "PAYMENT_REFUNDED", conta.TenantId, recebivel, 450m));
        Assert.Equal(SituacaoRecebivel.Aberto, await SituacaoDe(conta.TenantId, recebivel));

        /*
         * E então o aviso do pagamento chega de novo.
         *
         * Não é caso inventado: a entrega do Asaas é at least once, ele
         * reenvia, e guarda evento por 14 dias. Sem a tabela de eventos, este
         * reenvio daria baixa num recebível que foi legitimamente reaberto — e
         * o escritório pararia de cobrar alguém que deve.
         */
        await Avisar(Aviso("evt_pago", "PAYMENT_RECEIVED", conta.TenantId, recebivel, 450m));
        Assert.Equal(SituacaoRecebivel.Aberto, await SituacaoDe(conta.TenantId, recebivel));
    }

    [Fact]
    public async Task Estorno_reabre_em_vez_de_cancelar()
    {
        var (http, conta) = await Entrar();
        var recebivel = await CriarRecebivel(http);
        await http.PostAsync($"/recebiveis/{recebivel}/cobrar", null);

        await Avisar(Aviso("evt_1", "PAYMENT_RECEIVED", conta.TenantId, recebivel, 450m));
        await Avisar(Aviso("evt_2", "PAYMENT_REFUNDED", conta.TenantId, recebivel, 450m));

        var reaberto = await Buscar(conta.TenantId, recebivel);

        /*
         * Cancelar apagaria a cobrança da conta do escritório, que é o
         * contrário do que estorno significa: o dinheiro voltou, e o valor
         * continua devido.
         */
        Assert.Equal(SituacaoRecebivel.Aberto, reaberto.Situacao);
        Assert.Null(reaberto.ValorPago);
        Assert.Null(reaberto.PagoEm);
    }

    [Fact]
    public async Task Um_tenant_nao_baixa_o_recebivel_do_outro()
    {
        var (http, dono) = await Entrar();
        var recebivel = await CriarRecebivel(http);
        await http.PostAsync($"/recebiveis/{recebivel}/cobrar", null);

        var intruso = await Contas.Criar(_banco, _aplicacao);

        /*
         * Um aviso que nomeia o tenant errado para o recebível certo. O token
         * confere — é o mesmo — mas a política de linha não devolve a linha, e
         * não há nada a baixar. Quem protege aqui é o Postgres, e não um
         * `where` que alguém possa esquecer.
         */
        var resposta = await Avisar(Aviso("evt_1", "PAYMENT_RECEIVED", intruso.TenantId, recebivel, 450m));
        resposta.EnsureSuccessStatusCode();

        Assert.Equal(SituacaoRecebivel.Aberto, await SituacaoDe(dono.TenantId, recebivel));
    }

    [Fact]
    public async Task Aviso_que_nao_interessa_responde_200()
    {
        var (http, conta) = await Entrar();
        var recebivel = await CriarRecebivel(http);
        await http.PostAsync($"/recebiveis/{recebivel}/cobrar", null);

        /*
         * Responder erro aqui pararia a fila do webhook depois de 15 avisos
         * seguidos que não entendemos — e com ela os avisos de pagamento, que
         * são os que importam. Eventos parados somem em 14 dias.
         */
        var resposta = await Avisar(
            Aviso("evt_1", "PAYMENT_BANK_SLIP_VIEWED", conta.TenantId, recebivel, 450m));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(SituacaoRecebivel.Aberto, await SituacaoDe(conta.TenantId, recebivel));
    }

    /* --------------------------------------------------------- apoio */

    private async Task<(HttpClient, ContaDeTestes)> Entrar()
    {
        var conta = await Contas.Criar(_banco, _aplicacao);
        return (await Contas.Entrar(_aplicacao, conta), conta);
    }

    private async Task<HttpResponseMessage> Avisar(object aviso)
    {
        var cliente = _aplicacao.CreateClient();
        cliente.DefaultRequestHeaders.Add("asaas-access-token", TokenDoWebhook);
        return await cliente.PostAsJsonAsync("/integracoes/asaas/webhook", aviso);
    }

    private static object Aviso(
        string id, string evento, Guid tenant, Guid recebivel, decimal valor) => new
        {
            id,
            @event = evento,
            payment = new
            {
                id = "pay_000001",
                externalReference = $"{tenant}/{recebivel}",
                value = valor,
                status = evento == "PAYMENT_REFUNDED" ? "REFUNDED" : "RECEIVED",
                clientPaymentDate = "2026-03-08",
            },
        };

    private async Task<Recebivel> Buscar(Guid tenant, Guid id)
    {
        await using var contexto = _banco.Criar(tenant);
        return await contexto.Recebiveis.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private async Task<SituacaoRecebivel> SituacaoDe(Guid tenant, Guid id) =>
        (await Buscar(tenant, id)).Situacao;

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
        var resposta = await http.PostAsJsonAsync("/recebiveis", new DadosDoAvulso(
            pessoa, "Honorários", 450m, new DateOnly(ano, mes, 10), ano, mes), Json);

        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<RecebivelNaLista>(Json))!.Id;
    }

    private static async Task<Guid> CriarRecebivel(HttpClient http)
    {
        var pessoa = await CriarPessoa(http, Documentos.CnpjValido());
        return await CriarAvulso(http, pessoa, 2026, 3);
    }
}
