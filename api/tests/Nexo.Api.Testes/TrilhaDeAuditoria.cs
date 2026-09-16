using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;
using Npgsql;
using static Nexo.Api.Testes.Apoio;

namespace Nexo.Api.Testes;

/// <summary>
/// A trilha de auditoria: quem fez o quê, e quando.
///
/// <para>
/// O que se confere é o que faria a trilha mentir sem erro nenhum: a baixa que
/// não aparece, o movimento apagado que some sem deixar rastro, a marca do PSP
/// que muda de conta por fora do SaveChanges, a gravação sem mudança que polui o
/// histórico, o evento reescrito depois e o histórico lido por outro escritório.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class TrilhaDeAuditoria(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Baixa_e_estorno_ficam_no_historico_do_lancamento_com_quem_fez()
    {
        var http = await Entrar();
        var conta = await ContasBancariasDeTeste.Criar(http);
        var lancamento = await Lancar(http, 800m);

        (await http.PostAsJsonAsync($"/lancamentos/{lancamento}/baixar",
            new DadosDaBaixa(conta, 800m, new DateOnly(2026, 3, 10)), Json)).EnsureSuccessStatusCode();
        (await http.PostAsync($"/lancamentos/{lancamento}/estornar", null)).EnsureSuccessStatusCode();

        var historico = await Historico(http, $"/lancamentos/{lancamento}/historico");

        /* O movimento da baixa vem junto, e continua no histórico depois de o estorno o ter apagado. */
        Assert.Equal(
            ["Lançamento criado", "Baixa", "Movimento lançado", "Estorno", "Movimento apagado"],
            historico.Select(evento => evento.Resumo));
        Assert.All(historico, evento => Assert.Equal("Pessoa de teste", evento.Autor));

        var baixa = historico[1].Mudancas;
        Assert.Contains(new MudancaDeCampo("situacao", "Aberto", "Pago"), baixa);
        Assert.Contains(new MudancaDeCampo("valorPago", null, "800.00"), baixa);

        /* O carimbo de atualização muda em toda gravação e não conta nada: fica de fora. */
        Assert.DoesNotContain(baixa, mudanca => mudanca.Campo == "atualizadoEm");

        var apagado = historico[4];
        Assert.Equal(AcaoDeAuditoria.Apagado, apagado.Acao);
        Assert.Contains(new MudancaDeCampo("contaId", conta.ToString(), null), apagado.Mudancas);
    }

    [Fact]
    public async Task Gravar_sem_mudar_nada_nao_entra_no_historico()
    {
        var http = await Entrar();
        var lancamento = await Lancar(http, 300m);

        /* Tirar a classificação de quem não tem nenhuma grava e não muda campo nenhum. */
        (await http.PutAsJsonAsync($"/lancamentos/{lancamento}/classificacao",
            new DadosDaClassificacao(null, null), Json)).EnsureSuccessStatusCode();

        var historico = await Historico(http, $"/lancamentos/{lancamento}/historico");
        Assert.Equal("Lançamento criado", Assert.Single(historico).Resumo);
    }

    [Fact]
    public async Task A_conta_guarda_o_saldo_inicial_alterado_e_a_marca_do_psp_que_mudou_de_conta()
    {
        var http = await Entrar();
        var itau = await CriarConta(http, "Itaú PJ", recebeCobrancas: true);

        (await http.PutAsJsonAsync($"/contas-bancarias/{itau}", new DadosDaConta(
            "Itaú PJ", TipoConta.Corrente, "Itaú", "0001", "12345-6", 1_500m, new DateOnly(2026, 1, 1),
            Ativa: true, RecebeCobrancas: true), Json)).EnsureSuccessStatusCode();

        /* Marcar outra conta tira a marca desta por fora do SaveChanges, e mesmo assim fica no histórico dela. */
        await CriarConta(http, "Asaas", recebeCobrancas: true);

        var historico = await Historico(http, $"/contas-bancarias/{itau}/historico");

        Assert.Equal(["Conta cadastrada", "Conta alterada", "Conta alterada"], historico.Select(evento => evento.Resumo));
        Assert.Equal([new MudancaDeCampo("saldoInicial", "0.00", "1500.00")], historico[1].Mudancas);
        Assert.Equal([new MudancaDeCampo("recebeCobrancas", "true", "false")], historico[2].Mudancas);
        Assert.All(historico, evento => Assert.Equal("Pessoa de teste", evento.Autor));
    }

    [Fact]
    public async Task O_historico_nao_se_reescreve_e_nao_vaza_para_outro_escritorio()
    {
        var conta = await Contas.Criar(banco, _aplicacao);
        var http = await Contas.Entrar(_aplicacao, conta);
        var lancamento = await Lancar(http, 300m);

        var outroEscritorio = await Entrar();
        Assert.Equal(HttpStatusCode.NotFound,
            (await outroEscritorio.GetAsync($"/lancamentos/{lancamento}/historico")).StatusCode);

        /* Nem falando como o escritório dono dos eventos a trilha se deixa alterar ou apagar. */
        await using var contexto = banco.Criar(conta.TenantId);

        var alterar = await Record.ExceptionAsync(() => contexto.EventosDeAuditoria
            .ExecuteUpdateAsync(ajuste => ajuste.SetProperty(evento => evento.Autor, "Outra pessoa")));
        var apagar = await Record.ExceptionAsync(() => contexto.EventosDeAuditoria.ExecuteDeleteAsync());

        Assert.Contains("só recebe eventos novos", MensagemDoBanco(alterar));
        Assert.Contains("só recebe eventos novos", MensagemDoBanco(apagar));
        Assert.Equal(1, await contexto.EventosDeAuditoria.CountAsync(evento => evento.LancamentoId == lancamento));
    }

    /* --------------------------------------------------------- apoio */

    private async Task<HttpClient> Entrar() =>
        await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

    /// <summary>Um lançamento a receber, de um cliente novo, na competência de março.</summary>
    private static async Task<Guid> Lancar(HttpClient http, decimal valor)
    {
        var pessoa = await http.PostAsJsonAsync("/pessoas", new DadosDePessoa(
            TipoPessoa.Juridica, RegimeTributario.SimplesNacional, string.Empty, [Papel.Cliente],
            "Cliente do teste", string.Empty, Documentos.CnpjValido(), string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, null, string.Empty, true), Json);
        pessoa.EnsureSuccessStatusCode();
        var pessoaId = (await pessoa.Content.ReadFromJsonAsync<PessoaDetalhada>(Json))!.Id;

        var lancamento = await http.PostAsJsonAsync("/lancamentos", new DadosDoAvulso(
            NaturezaLancamento.Receber, pessoaId, "Honorários", valor, new DateOnly(2026, 3, 10), 2026, 3), Json);
        lancamento.EnsureSuccessStatusCode();
        return (await lancamento.Content.ReadFromJsonAsync<LancamentoNaLista>(Json))!.Id;
    }

    private static async Task<Guid> CriarConta(HttpClient http, string nome, bool recebeCobrancas)
    {
        var resposta = await http.PostAsJsonAsync("/contas-bancarias", new DadosDaConta(
            nome, TipoConta.Corrente, "Itaú", "0001", "12345-6", 0m, new DateOnly(2026, 1, 1),
            RecebeCobrancas: recebeCobrancas), Json);
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<ContaNaLista>(Json))!.Id;
    }

    private static async Task<List<EventoNoHistorico>> Historico(HttpClient http, string caminho) =>
        (await http.GetFromJsonAsync<List<EventoNoHistorico>>(caminho, Json))!;

    /// <summary>A mensagem que o Postgres deu, onde quer que ela esteja na cadeia de exceções.</summary>
    private static string MensagemDoBanco(Exception? erro)
    {
        for (var atual = erro; atual is not null; atual = atual.InnerException)
        {
            if (atual is PostgresException doBanco) return doBanco.MessageText;
        }

        return erro?.ToString() ?? "nenhum erro";
    }
}
