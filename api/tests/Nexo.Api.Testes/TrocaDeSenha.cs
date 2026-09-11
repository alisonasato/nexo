using System.Net;
using System.Net.Http.Json;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// A troca de senha, pela borda HTTP.
///
/// Existe porque a senha inicial vem das variáveis de provisionamento e, sem
/// isto, valia para sempre: quem provisionou conhecia a senha do único usuário.
/// Aceitável enquanto o usuário é o próprio dono; deixa de ser no minuto em que
/// houver alguém além dele.
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class TrocaDeSenha(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Trocar_a_senha_faz_a_antiga_parar_de_servir()
    {
        var conta = await Contas.Criar(banco, _aplicacao);
        var cliente = await Contas.Entrar(_aplicacao, conta);

        const string nova = "OutraSenhaForte@2027";

        var troca = await cliente.PostAsJsonAsync("/autenticacao/trocar-senha",
            new PedidoDeTrocaDeSenha(conta.Senha, nova));

        Assert.Equal(HttpStatusCode.NoContent, troca.StatusCode);

        var anonimo = _aplicacao.CreateClient();

        var comAntiga = await anonimo.PostAsJsonAsync("/autenticacao/entrar",
            new PedidoDeEntrada(conta.Email, conta.Senha));
        Assert.Equal(HttpStatusCode.Unauthorized, comAntiga.StatusCode);

        var comNova = await anonimo.PostAsJsonAsync("/autenticacao/entrar",
            new PedidoDeEntrada(conta.Email, nova));
        Assert.Equal(HttpStatusCode.OK, comNova.StatusCode);
    }

    [Fact]
    public async Task Trocar_a_senha_derruba_a_sessao_aberta_em_outro_navegador()
    {
        var conta = await Contas.Criar(banco, _aplicacao);

        /* Duas sessões da mesma pessoa: o computador do escritório e o de casa. */
        var noEscritorio = await Contas.Entrar(_aplicacao, conta);
        var emCasa = await Contas.Entrar(_aplicacao, conta);

        Assert.Equal(HttpStatusCode.OK, (await emCasa.GetAsync("/autenticacao/eu")).StatusCode);

        var troca = await noEscritorio.PostAsJsonAsync("/autenticacao/trocar-senha",
            new PedidoDeTrocaDeSenha(conta.Senha, "OutraSenhaForte@2027"));
        Assert.Equal(HttpStatusCode.NoContent, troca.StatusCode);

        /*
         * Este é o ponto. O token de casa continua assinado por nós e dentro do
         * prazo — só o carimbo não bate mais com o banco. Sem a conferência a
         * cada requisição, ele seguiria abrindo tudo por até oito horas, que é
         * exatamente o que quem troca a senha por suspeita de vazamento não
         * quer.
         */
        Assert.Equal(HttpStatusCode.Unauthorized, (await emCasa.GetAsync("/autenticacao/eu")).StatusCode);
    }

    [Fact]
    public async Task Quem_trocou_a_senha_segue_dentro_sem_entrar_de_novo()
    {
        var conta = await Contas.Criar(banco, _aplicacao);
        var cliente = await Contas.Entrar(_aplicacao, conta);

        var troca = await cliente.PostAsJsonAsync("/autenticacao/trocar-senha",
            new PedidoDeTrocaDeSenha(conta.Senha, "OutraSenhaForte@2027"));

        /* O cookie novo vem na mesma resposta, já com o carimbo novo dentro. */
        cliente.DefaultRequestHeaders.Remove("Cookie");
        cliente.DefaultRequestHeaders.Add("Cookie", Contas.CabecalhoDeCookie(troca).Split(';')[0]);

        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/autenticacao/eu")).StatusCode);
    }

    [Fact]
    public async Task A_resposta_traz_um_cookie_novo()
    {
        var conta = await Contas.Criar(banco, _aplicacao);
        var cliente = await Contas.Entrar(_aplicacao, conta);

        var troca = await cliente.PostAsJsonAsync("/autenticacao/trocar-senha",
            new PedidoDeTrocaDeSenha(conta.Senha, "OutraSenhaForte@2027"));

        /*
         * Sem cookie novo, a pessoa troca a senha e segue com o token antigo —
         * que funciona, mas some no fim do prazo sem que ela entenda por quê.
         */
        var cookie = Contas.CabecalhoDeCookie(troca);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Senha_atual_errada_e_recusada_e_o_erro_aponta_o_campo_certo()
    {
        var conta = await Contas.Criar(banco, _aplicacao);
        var cliente = await Contas.Entrar(_aplicacao, conta);

        var troca = await cliente.PostAsJsonAsync("/autenticacao/trocar-senha",
            new PedidoDeTrocaDeSenha("NaoEhAMinhaSenha@1", "OutraSenhaForte@2027"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, troca.StatusCode);

        var corpo = await troca.Content.ReadFromJsonAsync<RespostaComProblemas>();
        var problema = Assert.Single(corpo!.Problemas);

        // No campo da senha atual, não no da nova.
        Assert.Equal("senhaAtual", problema.Campo);
        Assert.Equal("A senha atual está incorreta.", problema.Descricao);

        // E a senha de verdade continua valendo.
        var anonimo = _aplicacao.CreateClient();
        var entrada = await anonimo.PostAsJsonAsync("/autenticacao/entrar",
            new PedidoDeEntrada(conta.Email, conta.Senha));
        Assert.Equal(HttpStatusCode.OK, entrada.StatusCode);
    }

    [Fact]
    public async Task Senha_fraca_e_recusada_com_a_mensagem_em_portugues()
    {
        var conta = await Contas.Criar(banco, _aplicacao);
        var cliente = await Contas.Entrar(_aplicacao, conta);

        var troca = await cliente.PostAsJsonAsync("/autenticacao/trocar-senha",
            new PedidoDeTrocaDeSenha(conta.Senha, "abc"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, troca.StatusCode);

        var corpo = await troca.Content.ReadFromJsonAsync<RespostaComProblemas>();

        Assert.All(corpo!.Problemas, problema => Assert.Equal("senhaNova", problema.Campo));

        /*
         * O padrão do Identity é inglês. Num sistema todo em português, isso é
         * a diferença entre uma instrução e um ruído — daí o DescritorDeErros.
         */
        Assert.Contains(corpo.Problemas, problema => problema.Descricao.Contains("caracteres"));
        Assert.DoesNotContain(corpo.Problemas, problema => problema.Descricao.Contains("Password"));
    }

    [Fact]
    public async Task Repetir_a_senha_atual_como_nova_e_recusado()
    {
        var conta = await Contas.Criar(banco, _aplicacao);
        var cliente = await Contas.Entrar(_aplicacao, conta);

        var troca = await cliente.PostAsJsonAsync("/autenticacao/trocar-senha",
            new PedidoDeTrocaDeSenha(conta.Senha, conta.Senha));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, troca.StatusCode);

        var corpo = await troca.Content.ReadFromJsonAsync<RespostaComProblemas>();
        Assert.Contains(corpo!.Problemas, problema => problema.Descricao.Contains("igual à atual"));
    }

    [Fact]
    public async Task Sem_sessao_nao_se_troca_senha_de_ninguem()
    {
        var anonimo = _aplicacao.CreateClient();

        var troca = await anonimo.PostAsJsonAsync("/autenticacao/trocar-senha",
            new PedidoDeTrocaDeSenha("qualquer", "OutraSenhaForte@2027"));

        Assert.Equal(HttpStatusCode.Unauthorized, troca.StatusCode);
    }
}
