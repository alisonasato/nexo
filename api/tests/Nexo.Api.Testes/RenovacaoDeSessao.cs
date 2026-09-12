using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Nexo.Api.Autenticacao;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// A sessão que se estende sozinha enquanto alguém está trabalhando.
///
/// <para>
/// O token vale oito horas. Antes disto, quem passasse do prazo caía para a
/// tela de entrada no meio do trabalho — e um ERP é usado justamente por
/// jornadas inteiras.
/// </para>
/// <para>
/// O relógio destes testes é falso, e precisa ser: provar que um token perto
/// do fim é renovado e um recém-assinado não é exigiria esperar horas de
/// verdade. Ele começa no agora real para que o token continue válido aos
/// olhos do validador de assinatura, que usa o relógio do sistema.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class RenovacaoDeSessao : IDisposable
{
    private readonly BancoDeTestes _banco;
    private readonly FakeTimeProvider _relogio = new(DateTimeOffset.UtcNow);
    private readonly AplicacaoDeTestes _aplicacao;

    public RenovacaoDeSessao(BancoDeTestes banco)
    {
        _banco = banco;

        /* O relógio falso entra no lugar do do sistema; o resto da aplicação é o mesmo. */
        _aplicacao = new AplicacaoDeTestes(
            banco.Conexao,
            servicos => servicos.AddSingleton<TimeProvider>(_relogio));
    }

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Token_recem_assinado_nao_e_renovado()
    {
        var http = await Entrar();

        var resposta = await http.GetAsync("/autenticacao/eu");
        resposta.EnsureSuccessStatusCode();

        /*
         * Renovar a cada requisição custaria uma assinatura por requisição e
         * reescreveria o cookie o tempo todo, sem ganho: o prazo está longe.
         */
        Assert.Null(CookieDeSessao(resposta));
    }

    [Fact]
    public async Task Passada_a_metade_do_prazo_a_sessao_se_estende()
    {
        var http = await Entrar();

        /* Cinco horas de uma jornada de oito: falta menos que a metade. */
        _relogio.Advance(TimeSpan.FromHours(5));

        var resposta = await http.GetAsync("/autenticacao/eu");
        resposta.EnsureSuccessStatusCode();

        var renovado = CookieDeSessao(resposta);
        Assert.NotNull(renovado);

        /* E o token novo abre porta sozinho. */
        var comONovo = _aplicacao.CreateClient();
        comONovo.DefaultRequestHeaders.Add("Cookie", Sessao.Cookie + "=" + renovado);
        Assert.Equal(HttpStatusCode.OK, (await comONovo.GetAsync("/autenticacao/eu")).StatusCode);
    }

    [Fact]
    public async Task A_renovacao_carrega_o_tenant_junto()
    {
        var conta = await Contas.Criar(_banco, _aplicacao);
        var http = await Entrar(conta);

        _relogio.Advance(TimeSpan.FromHours(5));

        var resposta = await http.GetAsync("/autenticacao/eu");
        var renovado = CookieDeSessao(resposta);

        var comONovo = _aplicacao.CreateClient();
        comONovo.DefaultRequestHeaders.Add("Cookie", Sessao.Cookie + "=" + renovado);

        /*
         * O tenant vive no token e alimenta a política de RLS. Uma renovação
         * que o perdesse não devolveria erro: devolveria uma lista vazia, que
         * é muito pior — parece cadastro sem ninguém.
         */
        var sessao = await comONovo.GetFromJsonAsync<SessaoAtual>("/autenticacao/eu");
        Assert.Equal(conta.TenantId, sessao!.TenantId);
    }

    [Fact]
    public async Task Passado_o_teto_a_sessao_deixa_de_ser_estendida()
    {
        var http = await Entrar();

        /*
         * Oito dias, além do teto de sete. Sem o teto, uma aba esquecida aberta
         * sustentaria a mesma sessão para sempre, e um cookie roubado com ela.
         */
        _relogio.Advance(TimeSpan.FromDays(8));

        var resposta = await http.GetAsync("/autenticacao/eu");

        /* O token atual ainda vale — quem está no meio de um lançamento não
           perde o trabalho —, mas ninguém estende mais. */
        resposta.EnsureSuccessStatusCode();
        Assert.Null(CookieDeSessao(resposta));
    }

    [Fact]
    public async Task Trocar_a_senha_derruba_mesmo_na_hora_de_renovar()
    {
        var conta = await Contas.Criar(_banco, _aplicacao);
        var http = await Entrar(conta);

        /* Outro navegador troca a senha, e com ela o carimbo de segurança. */
        var outro = await Contas.Entrar(_aplicacao, conta);
        var troca = await outro.PostAsJsonAsync("/autenticacao/trocar-senha",
            new PedidoDeTrocaDeSenha(conta.Senha, "OutraSenhaForte@2026"));
        troca.EnsureSuccessStatusCode();

        _relogio.Advance(TimeSpan.FromHours(5));

        var resposta = await http.GetAsync("/autenticacao/eu");

        /*
         * A conferência do carimbo vem antes da renovação, e é por isso que
         * esta requisição cai. Na ordem inversa, a sessão antiga sairia daqui
         * com prazo novo em folha — exatamente o contrário do que quem troca a
         * senha por suspeita de vazamento espera.
         */
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Null(CookieDeSessao(resposta));
    }

    /* --------------------------------------------------------- apoio */

    private async Task<HttpClient> Entrar(ContaDeTestes? conta = null)
    {
        conta ??= await Contas.Criar(_banco, _aplicacao);
        return await Contas.Entrar(_aplicacao, conta);
    }

    /// <summary>O valor do cookie de sessão que a resposta mandou gravar, se mandou.</summary>
    private static string? CookieDeSessao(HttpResponseMessage resposta)
    {
        if (!resposta.Headers.TryGetValues("Set-Cookie", out var cabecalhos)) return null;

        var cookie = cabecalhos.FirstOrDefault(valor => valor.StartsWith(Sessao.Cookie + "="));
        if (cookie is null) return null;

        var valorDele = cookie.Split(';')[0][(Sessao.Cookie.Length + 1)..];

        /* Sair também manda um Set-Cookie, com valor vazio: não é renovação. */
        return string.IsNullOrEmpty(valorDele) ? null : valorDele;
    }
}
