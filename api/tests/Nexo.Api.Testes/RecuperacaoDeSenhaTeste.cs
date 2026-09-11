using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Nexo.Api.Dados;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// A saída de emergência para quem perdeu a senha.
///
/// <para>
/// Não existe recuperação de senha no sistema, e o provisionamento não ajuda:
/// ele só age com o banco vazio. Sem isto, perder a senha trancava o sistema
/// para sempre, com os dados lá dentro.
/// </para>
/// <para>
/// É uma porta de emergência de verdade — abre com variável de ambiente, que é
/// o único lugar que quem implanta num PaaS alcança —, e por isso precisa de
/// teste: a hora de descobrir que ela não funciona não pode ser a hora em que
/// ela é a única opção.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class RecuperacaoDeSenhaTeste(BancoDeTestes banco) : IDisposable
{
    private readonly List<AplicacaoDeTestes> _abertas = [];

    public void Dispose()
    {
        foreach (var aplicacao in _abertas) aplicacao.Dispose();
    }

    private AplicacaoDeTestes Nova()
    {
        var aplicacao = new AplicacaoDeTestes(banco.Conexao);
        _abertas.Add(aplicacao);
        return aplicacao;
    }

    [Fact]
    public async Task Redefine_a_senha_e_a_antiga_para_de_servir()
    {
        var aplicacao = Nova();
        var conta = await Contas.Criar(banco, aplicacao);

        const string nova = "SenhaRecuperada@2027";

        await RecuperacaoDeSenha.ExecutarAsync(
            aplicacao.Services, new RecuperacaoDeSenha.Pedido(conta.Email, nova));

        var http = aplicacao.CreateClient();

        var comAntiga = await http.PostAsJsonAsync("/autenticacao/entrar",
            new PedidoDeEntrada(conta.Email, conta.Senha));
        Assert.Equal(HttpStatusCode.Unauthorized, comAntiga.StatusCode);

        var comNova = await http.PostAsJsonAsync("/autenticacao/entrar",
            new PedidoDeEntrada(conta.Email, nova));
        Assert.Equal(HttpStatusCode.OK, comNova.StatusCode);
    }

    [Fact]
    public async Task Derruba_as_sessoes_que_estavam_abertas()
    {
        var aplicacao = Nova();
        var conta = await Contas.Criar(banco, aplicacao);

        var jaDentro = await Contas.Entrar(aplicacao, conta);
        Assert.Equal(HttpStatusCode.OK, (await jaDentro.GetAsync("/autenticacao/eu")).StatusCode);

        await RecuperacaoDeSenha.ExecutarAsync(
            aplicacao.Services, new RecuperacaoDeSenha.Pedido(conta.Email, "SenhaRecuperada@2027"));

        /*
         * Quem perdeu a senha não sabe quem mais está dentro — pode ser
         * justamente por isso que ela se perdeu. A troca roda o carimbo de
         * segurança, e toda sessão aberta cai junto.
         */
        Assert.Equal(HttpStatusCode.Unauthorized, (await jaDentro.GetAsync("/autenticacao/eu")).StatusCode);
    }

    [Fact]
    public async Task E_mail_desconhecido_nao_cria_usuario_nenhum()
    {
        var aplicacao = Nova();
        var conta = await Contas.Criar(banco, aplicacao);

        await RecuperacaoDeSenha.ExecutarAsync(
            aplicacao.Services,
            new RecuperacaoDeSenha.Pedido("ninguem@exemplo.invalido", "SenhaQualquer@2027"));

        var http = aplicacao.CreateClient();

        /*
         * Criar aqui deixaria alguém dentro do sistema sem escritório nenhum, e
         * um usuário sem tenant não enxerga nada — parece defeito, e é pior:
         * parece que a recuperação funcionou.
         */
        var tentativa = await http.PostAsJsonAsync("/autenticacao/entrar",
            new PedidoDeEntrada("ninguem@exemplo.invalido", "SenhaQualquer@2027"));
        Assert.Equal(HttpStatusCode.Unauthorized, tentativa.StatusCode);

        /* E o usuário que existe continua entrando com a senha dele. */
        var original = await http.PostAsJsonAsync("/autenticacao/entrar",
            new PedidoDeEntrada(conta.Email, conta.Senha));
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
    }

    [Fact]
    public async Task Senha_fraca_e_recusada_e_a_antiga_continua_valendo()
    {
        var aplicacao = Nova();
        var conta = await Contas.Criar(banco, aplicacao);

        await RecuperacaoDeSenha.ExecutarAsync(
            aplicacao.Services, new RecuperacaoDeSenha.Pedido(conta.Email, "123"));

        var http = aplicacao.CreateClient();

        var fraca = await http.PostAsJsonAsync("/autenticacao/entrar",
            new PedidoDeEntrada(conta.Email, "123"));
        Assert.Equal(HttpStatusCode.Unauthorized, fraca.StatusCode);

        /*
         * O ponto: a senha antiga precisa continuar valendo. A redefinição tira
         * a senha antes de pôr a nova, e se a nova for recusada sem isto o
         * usuário ficaria sem senha nenhuma — trancado de vez, pela ferramenta
         * que existe justamente para destrancar.
         */
        var antiga = await http.PostAsJsonAsync("/autenticacao/entrar",
            new PedidoDeEntrada(conta.Email, conta.Senha));
        Assert.Equal(HttpStatusCode.OK, antiga.StatusCode);
    }

    [Fact]
    public void Sem_as_duas_variaveis_nao_ha_nada_a_fazer()
    {
        var vazia = new ConfigurationBuilder().Build();
        Assert.Null(RecuperacaoDeSenha.Ler(vazia));

        /* Só o e-mail não basta: metade da configuração é engano, não pedido. */
        var soEmail = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Recuperacao:Email"] = "alguem@exemplo.com",
            })
            .Build();

        Assert.Null(RecuperacaoDeSenha.Ler(soEmail));

        var completa = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Recuperacao:Email"] = "  alguem@exemplo.com  ",
                ["Recuperacao:Senha"] = "SenhaForte@2027",
            })
            .Build();

        var pedido = RecuperacaoDeSenha.Ler(completa);
        Assert.NotNull(pedido);

        /* Espaço colado ao redor do e-mail é o erro mais comum de quem cola num
           campo de painel, e não pode custar uma hora de depuração. */
        Assert.Equal("alguem@exemplo.com", pedido.Email);
    }
}
