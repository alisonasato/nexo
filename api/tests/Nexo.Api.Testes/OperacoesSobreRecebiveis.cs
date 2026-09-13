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
/// Parcelamento, renegociação e baixa em lote, pela borda HTTP.
///
/// <para>
/// É dinheiro sendo dividido, trocado e baixado aos montes, então os testes
/// conferem o que custa caro quando erra: centavo que some na divisão,
/// mensalidade cobrada de novo depois de um acordo, título pago baixado por cima
/// no lote, e cobrança que continua pagável depois de o título mudar.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class OperacoesSobreRecebiveis : IDisposable
{
    private const string TokenDoWebhook = "token-combinado-com-o-psp";

    private readonly BancoDeTestes _banco;
    private readonly AsaasDeMentira _psp = new();
    private readonly AplicacaoDeTestes _aplicacao;

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public OperacoesSobreRecebiveis(BancoDeTestes banco)
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

    /* ------------------------------------------------------ parcelamento */

    [Fact]
    public async Task Parcelas_somam_exatamente_o_total()
    {
        var (http, _) = await Entrar();
        var pessoa = await CriarPessoa(http);

        var criado = await Parcelar(http, pessoa, 100m, 3, new DateOnly(2026, 3, 10));

        /*
         * Dividir em decimal e arredondar cada uma daria três vezes 33,33, e um
         * centavo sumiria. Os centavos da sobra vão para a primeira.
         */
        Assert.Equal([33.34m, 33.33m, 33.33m], criado.Parcelas.Select(p => p.Valor).ToList());
        Assert.Equal(100m, criado.Parcelas.Sum(p => p.Valor));

        Assert.Equal([1, 2, 3], criado.Parcelas.Select(p => p.ParcelaNumero ?? 0).ToList());
        Assert.All(criado.Parcelas, parcela => Assert.Equal(3, parcela.ParcelasTotal));
    }

    [Fact]
    public async Task Vencimentos_partem_da_primeira_data_e_nao_da_parcela_anterior()
    {
        var (http, _) = await Entrar();
        var pessoa = await CriarPessoa(http);

        var criado = await Parcelar(http, pessoa, 90m, 3, new DateOnly(2026, 1, 31));

        /* Somando um mês à parcela anterior, março grudaria no dia 28. */
        Assert.Equal(
            [new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31)],
            criado.Parcelas.Select(p => p.Vencimento).ToList());
    }

    [Fact]
    public async Task Parcelamento_fora_do_intervalo_e_recusado()
    {
        var (http, _) = await Entrar();
        var pessoa = await CriarPessoa(http);

        foreach (var quantidade in new[] { 0, Parcelas.Maximo + 1 })
        {
            var resposta = await http.PostAsJsonAsync("/recebiveis/parcelamentos",
                new DadosDoParcelamento(pessoa, "Abertura de empresa", 600m, quantidade,
                    new DateOnly(2026, 3, 10), 2026, 3), Json);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        }
    }

    /* ------------------------------------------------------ renegociação */

    [Fact]
    public async Task Renegociar_substitui_o_titulo_por_parcelas_novas()
    {
        var (http, conta) = await Entrar();
        var pessoa = await CriarPessoa(http);
        var recebivel = await CriarAvulso(http, pessoa, 450m, new DateOnly(2026, 3, 10));

        var acordo = await Renegociar(http, recebivel,
            new DadosDaRenegociacao(2, new DateOnly(2026, 4, 10), 30m, 9m, 9m, "Cliente pediu mais prazo"));

        /* 450 mais 30 de juros e 9 de multa, menos 9 de desconto: 480 em duas. */
        Assert.Equal(recebivel, acordo.OrigemId);
        Assert.Equal([240m, 240m], acordo.Parcelas.Select(p => p.Valor).ToList());

        Assert.All(acordo.Parcelas, parcela =>
        {
            Assert.Equal(recebivel, parcela.RenegociadoDeId);
            Assert.Equal(SituacaoRecebivel.Aberto, parcela.Situacao);
        });

        /* O título original não é editado nem apagado: vira o histórico do acordo. */
        var original = await Buscar(conta.TenantId, recebivel);
        Assert.Equal(SituacaoRecebivel.Renegociado, original.Situacao);
        Assert.Equal(450m, original.Valor);
    }

    [Fact]
    public async Task Mensalidade_renegociada_continua_ocupando_a_competencia()
    {
        var (http, conta) = await Entrar();
        var pessoa = await CriarPessoa(http);

        var contrato = await http.PostAsJsonAsync("/contratos", new DadosDeContrato(
            pessoa, "Honorários contábeis", 500m, 10, new DateOnly(2025, 1, 1), null,
            SituacaoContrato.Ativo, string.Empty), Json);
        contrato.EnsureSuccessStatusCode();

        await Gerar(http, 2026, 3);
        var mensalidade = Assert.Single((await Listar(http, "?ano=2026&mes=3")).Itens).Id;

        await Renegociar(http, mensalidade,
            new DadosDaRenegociacao(2, new DateOnly(2026, 4, 10), 0m, 0m, 0m, "Cliente pediu prazo"));

        /*
         * Gerar de novo a mesma competência não pode cobrar outra vez. Se
         * renegociar cancelasse o título, a competência estaria livre, e o
         * cliente receberia a mensalidade que ele acabou de parcelar.
         */
        var segunda = await Gerar(http, 2026, 3);
        Assert.Equal(0, segunda.Geradas);

        await using var contexto = _banco.Criar(conta.TenantId);
        var doContrato = await contexto.Recebiveis.AsNoTracking()
            .Where(r => r.ContratoId != null && r.CompetenciaAno == 2026 && r.CompetenciaMes == 3)
            .ToListAsync();

        Assert.Equal(SituacaoRecebivel.Renegociado, Assert.Single(doContrato).Situacao);
    }

    [Fact]
    public async Task Renegociar_titulo_cobrado_tira_a_cobranca_do_psp()
    {
        var (http, _) = await Entrar();
        var pessoa = await CriarPessoa(http);
        var recebivel = await CriarAvulso(http, pessoa, 450m, new DateOnly(2026, 3, 10));
        await http.PostAsync($"/recebiveis/{recebivel}/cobrar", null);

        await Renegociar(http, recebivel,
            new DadosDaRenegociacao(1, new DateOnly(2026, 4, 10), 0m, 0m, 0m, "Acordo"));

        /* O boleto do título antigo, pagável, seria cobrar duas vezes o mesmo acordo. */
        Assert.Equal(["pay_000001"], _psp.CobrancasExcluidas);
    }

    [Fact]
    public async Task Renegociar_duas_vezes_e_recusado()
    {
        var (http, _) = await Entrar();
        var pessoa = await CriarPessoa(http);
        var recebivel = await CriarAvulso(http, pessoa, 450m, new DateOnly(2026, 3, 10));

        var pedido = new DadosDaRenegociacao(2, new DateOnly(2026, 4, 10), 0m, 0m, 0m, "Acordo");
        await Renegociar(http, recebivel, pedido);

        var segunda = await http.PostAsJsonAsync($"/recebiveis/{recebivel}/renegociar", pedido, Json);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, segunda.StatusCode);
    }

    [Fact]
    public async Task O_banco_recusa_uma_segunda_renegociacao_do_mesmo_titulo()
    {
        var (http, conta) = await Entrar();
        var pessoa = await CriarPessoa(http);
        var recebivel = await CriarAvulso(http, pessoa, 450m, new DateOnly(2026, 3, 10));

        /*
         * Direto no banco, sem passar pela situação: é a segunda defesa, para o
         * caso de dois cliques chegarem juntos. A trava de concorrência pega o
         * caso comum; o índice único pega o que escapar dela.
         */
        await using var contexto = _banco.Criar(conta.TenantId);

        contexto.Renegociacoes.Add(new Renegociacao
        {
            Id = Guid.NewGuid(), TenantId = conta.TenantId, OrigemId = recebivel, Motivo = "Primeira",
        });
        contexto.Renegociacoes.Add(new Renegociacao
        {
            Id = Guid.NewGuid(), TenantId = conta.TenantId, OrigemId = recebivel, Motivo = "Segunda",
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync());
    }

    [Fact]
    public async Task Desconto_maior_que_o_titulo_e_recusado_sem_mudar_nada()
    {
        var (http, conta) = await Entrar();
        var pessoa = await CriarPessoa(http);
        var recebivel = await CriarAvulso(http, pessoa, 450m, new DateOnly(2026, 3, 10));

        var resposta = await http.PostAsJsonAsync($"/recebiveis/{recebivel}/renegociar",
            new DadosDaRenegociacao(1, new DateOnly(2026, 4, 10), 0m, 0m, 500m, "Perdão"), Json);

        /* Perdoar a dívida inteira é cancelar com motivo, e não renegociar para zero. */
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal(SituacaoRecebivel.Aberto, (await Buscar(conta.TenantId, recebivel)).Situacao);
    }

    /* ---------------------------------------------------- baixa em lote */

    [Fact]
    public async Task Baixa_em_lote_baixa_o_que_esta_em_aberto_e_diz_o_que_recusou()
    {
        var (http, conta) = await Entrar();
        var pessoa = await CriarPessoa(http);

        var primeiro = await CriarAvulso(http, pessoa, 100m, new DateOnly(2026, 3, 10));
        var segundo = await CriarAvulso(http, pessoa, 200m, new DateOnly(2026, 3, 10));
        var jaPago = await CriarAvulso(http, pessoa, 300m, new DateOnly(2026, 3, 10));

        var manual = await http.PostAsJsonAsync($"/recebiveis/{jaPago}/baixar",
            new DadosDaBaixa(300m, new DateOnly(2026, 3, 5)), Json);
        manual.EnsureSuccessStatusCode();

        var resposta = await http.PostAsJsonAsync("/recebiveis/baixar-em-lote",
            new DadosDaBaixaEmLote([primeiro, segundo, jaPago], new DateOnly(2026, 3, 12)), Json);
        resposta.EnsureSuccessStatusCode();

        var resultado = await resposta.Content.ReadFromJsonAsync<ResultadoDaBaixaEmLote>(Json);

        /* Um já pago não impede os outros, e a resposta diz qual e por quê. */
        Assert.Equal(2, resultado!.Baixados);
        Assert.Equal(jaPago, Assert.Single(resultado.Recusados).Id);

        var baixado = await Buscar(conta.TenantId, primeiro);
        Assert.Equal(OrigensDeBaixa.Lote, baixado.OrigemDaBaixa);
        Assert.Equal(100m, baixado.ValorPago);
        Assert.Equal(new DateOnly(2026, 3, 12), baixado.PagoEm);

        /* O que já estava pago não foi baixado de novo por cima. */
        var intacto = await Buscar(conta.TenantId, jaPago);
        Assert.Equal(OrigensDeBaixa.Manual, intacto.OrigemDaBaixa);
        Assert.Equal(new DateOnly(2026, 3, 5), intacto.PagoEm);
    }

    [Fact]
    public async Task Baixa_em_lote_tira_as_cobrancas_do_psp()
    {
        var (http, _) = await Entrar();
        var pessoa = await CriarPessoa(http);
        var cobrado = await CriarAvulso(http, pessoa, 450m, new DateOnly(2026, 3, 10));
        await http.PostAsync($"/recebiveis/{cobrado}/cobrar", null);

        var resposta = await http.PostAsJsonAsync("/recebiveis/baixar-em-lote",
            new DadosDaBaixaEmLote([cobrado], null), Json);
        resposta.EnsureSuccessStatusCode();

        Assert.Equal(["pay_000001"], _psp.CobrancasExcluidas);
    }

    /* ------------------------------------------------------ divergências */

    [Fact]
    public async Task Divergencias_listam_o_pagamento_nao_aplicado_so_para_o_proprio_tenant()
    {
        var (http, conta) = await Entrar();
        var pessoa = await CriarPessoa(http, "Cliente divergente");
        var recebivel = await CriarAvulso(http, pessoa, 450m, new DateOnly(2026, 3, 10));
        await http.PostAsync($"/recebiveis/{recebivel}/cobrar", null);

        var cancelamento = await http.PostAsJsonAsync($"/recebiveis/{recebivel}/cancelar",
            new DadosDoCancelamento("Cliente desistiu"), Json);
        cancelamento.EnsureSuccessStatusCode();

        var aviso = await Avisar(Aviso("evt_tardio", "PAYMENT_RECEIVED", conta.TenantId, recebivel, 450m));
        aviso.EnsureSuccessStatusCode();

        var divergencias = await http.GetFromJsonAsync<List<DivergenciaDeCobranca>>(
            "/cobrancas/divergencias", Json);

        var unica = Assert.Single(divergencias!);
        Assert.Equal(recebivel, unica.RecebivelId);
        Assert.Equal("Cliente divergente", unica.NomeDaPessoa);

        var (outro, _) = await Entrar();
        Assert.Empty((await outro.GetFromJsonAsync<List<DivergenciaDeCobranca>>(
            "/cobrancas/divergencias", Json))!);
    }

    /* ---------------------------------------------------------- filtros */

    [Fact]
    public async Task Busca_e_periodo_de_vencimento_recortam_a_lista()
    {
        var (http, _) = await Entrar();
        var ana = await CriarPessoa(http, "Ana Contabilidade");
        var bruno = await CriarPessoa(http, "Bruno Comércio");

        await CriarAvulso(http, ana, 100m, new DateOnly(2026, 3, 5));
        await CriarAvulso(http, bruno, 200m, new DateOnly(2026, 4, 5));

        var porNome = await Listar(http, "?busca=bruno");
        Assert.Equal("Bruno Comércio", Assert.Single(porNome.Itens).NomeDaPessoa);

        var porPeriodo = await Listar(http, "?vencimentoDe=2026-03-01&vencimentoAte=2026-03-31");
        Assert.Equal(100m, Assert.Single(porPeriodo.Itens).Valor);
    }

    /* --------------------------------------------------------- apoio */

    private async Task<(HttpClient, ContaDeTestes)> Entrar()
    {
        var conta = await Contas.Criar(_banco, _aplicacao);
        return (await Contas.Entrar(_aplicacao, conta), conta);
    }

    private static async Task<Guid> CriarPessoa(HttpClient http, string nome = "Cliente do teste")
    {
        var resposta = await http.PostAsJsonAsync("/pessoas", new DadosDePessoa(
            TipoPessoa.Juridica, RegimeTributario.SimplesNacional, string.Empty, [Papel.Cliente],
            nome, string.Empty, Documentos.CnpjValido(), string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, null, string.Empty, true), Json);

        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<PessoaDetalhada>(Json))!.Id;
    }

    private static async Task<Guid> CriarAvulso(HttpClient http, Guid pessoa, decimal valor, DateOnly vencimento)
    {
        var resposta = await http.PostAsJsonAsync("/recebiveis",
            new DadosDoAvulso(pessoa, "Honorários", valor, vencimento, 2026, 3), Json);

        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<RecebivelNaLista>(Json))!.Id;
    }

    private static async Task<ParcelamentoCriado> Parcelar(
        HttpClient http, Guid pessoa, decimal total, int parcelas, DateOnly primeiroVencimento)
    {
        var resposta = await http.PostAsJsonAsync("/recebiveis/parcelamentos",
            new DadosDoParcelamento(pessoa, "Abertura de empresa", total, parcelas, primeiroVencimento, 2026, 3),
            Json);

        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<ParcelamentoCriado>(Json))!;
    }

    private static async Task<RenegociacaoCriada> Renegociar(HttpClient http, Guid id, DadosDaRenegociacao dados)
    {
        var resposta = await http.PostAsJsonAsync($"/recebiveis/{id}/renegociar", dados, Json);
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<RenegociacaoCriada>(Json))!;
    }

    private static async Task<ResultadoDaGeracao> Gerar(HttpClient http, int ano, int mes)
    {
        var resposta = await http.PostAsJsonAsync("/contratos/gerar-mensalidades",
            new PedidoDeGeracao(ano, mes, null), Json);

        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<ResultadoDaGeracao>(Json))!;
    }

    private static async Task<PaginaDeRecebiveis> Listar(HttpClient http, string consulta) =>
        (await http.GetFromJsonAsync<PaginaDeRecebiveis>("/recebiveis" + consulta, Json))!;

    private async Task<Recebivel> Buscar(Guid tenant, Guid id)
    {
        await using var contexto = _banco.Criar(tenant);
        return await contexto.Recebiveis.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private async Task<HttpResponseMessage> Avisar(object aviso)
    {
        var cliente = _aplicacao.CreateClient();
        cliente.DefaultRequestHeaders.Add("asaas-access-token", TokenDoWebhook);
        return await cliente.PostAsJsonAsync("/integracoes/asaas/webhook", aviso);
    }

    private static object Aviso(string id, string evento, Guid tenant, Guid recebivel, decimal valor) => new
    {
        id,
        @event = evento,
        payment = new
        {
            id = "pay_000001",
            externalReference = $"{tenant}/{recebivel}",
            value = valor,
            status = "RECEIVED",
            clientPaymentDate = "2026-03-08",
        },
    };
}
