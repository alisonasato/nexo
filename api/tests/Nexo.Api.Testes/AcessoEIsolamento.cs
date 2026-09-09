using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Api.Autenticacao;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// O passo 3 visto de fora: entrar, receber o cookie, e a claim do token
/// chegar até a política de RLS do Postgres.
///
/// Os testes de <see cref="IsolamentoEntreTenants"/> provam que a política
/// funciona quando alguém informa o tenant. Estes provam a outra metade: que
/// quem informa o tenant é a sessão, e ninguém mais.
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class AcessoEIsolamento(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Entrar_com_a_senha_certa_devolve_o_cookie_da_sessao()
    {
        var conta = await CriarContaEmTenantNovo();
        var cliente = _aplicacao.CreateClient();

        var resposta = await cliente.PostAsJsonAsync("/autenticacao/entrar",
            new PedidoDeEntrada(conta.Email, conta.Senha));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        var cookie = LerCookieDeSessao(resposta);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Entrar_com_a_senha_errada_e_recusado_sem_dizer_o_motivo()
    {
        var conta = await CriarContaEmTenantNovo();
        var cliente = _aplicacao.CreateClient();

        var resposta = await cliente.PostAsJsonAsync("/autenticacao/entrar",
            new PedidoDeEntrada(conta.Email, "SenhaErrada@123"));

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);

        // A mesma frase para senha errada e para e-mail inexistente: distinguir
        // os dois entrega a lista de quem tem conta.
        var falha = await resposta.Content.ReadFromJsonAsync<FalhaDeEntrada>();
        Assert.Equal("E-mail ou senha incorretos.", falha!.Mensagem);

        var inexistente = await cliente.PostAsJsonAsync("/autenticacao/entrar",
            new PedidoDeEntrada("ninguem@lugar-nenhum.com.br", "SenhaErrada@123"));
        var mesmaFalha = await inexistente.Content.ReadFromJsonAsync<FalhaDeEntrada>();
        Assert.Equal(falha.Mensagem, mesmaFalha!.Mensagem);
    }

    [Fact]
    public async Task Sem_sessao_a_listagem_de_empresas_e_recusada()
    {
        var cliente = _aplicacao.CreateClient();

        var resposta = await cliente.GetAsync("/empresas");

        // A política padrão é fechada: rota nova nasce exigindo sessão.
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task A_saude_continua_aberta()
    {
        var cliente = _aplicacao.CreateClient();

        var resposta = await cliente.GetAsync("/saude");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task Com_sessao_so_vem_a_empresa_do_tenant_de_quem_entrou()
    {
        var contaA = await CriarContaEmTenantNovo();
        var contaB = await CriarContaEmTenantNovo();

        var empresasDeA = await ListarEmpresasComo(contaA);
        var empresasDeB = await ListarEmpresasComo(contaB);

        /*
         * O endpoint /empresas não tem filtro de tenant nenhum na consulta.
         * Se este teste passar, foi a política do Postgres que filtrou, e a
         * claim do cookie que disse a ela quem estava perguntando. É a corrente
         * inteira — cookie, token, claim, interceptor, RLS — verificada de uma
         * ponta à outra.
         */
        Assert.Single(empresasDeA);
        Assert.Single(empresasDeB);
        Assert.Equal(contaA.RazaoSocial, empresasDeA[0].RazaoSocial);
        Assert.Equal(contaB.RazaoSocial, empresasDeB[0].RazaoSocial);
        Assert.NotEqual(empresasDeA[0].Id, empresasDeB[0].Id);
    }

    [Fact]
    public async Task Cookie_adulterado_nao_abre_a_sessao()
    {
        var conta = await CriarContaEmTenantNovo();
        var cliente = _aplicacao.CreateClient();

        var entrada = await cliente.PostAsJsonAsync("/autenticacao/entrar",
            new PedidoDeEntrada(conta.Email, conta.Senha));
        var token = ValorDoCookie(LerCookieDeSessao(entrada));

        // Trocar o fim da assinatura basta para invalidar o token.
        var adulterado = token[..^2] + (token.EndsWith("AA", StringComparison.Ordinal) ? "BB" : "AA");

        var pedido = new HttpRequestMessage(HttpMethod.Get, "/empresas");
        pedido.Headers.Add("Cookie", Sessao.Cookie + "=" + adulterado);
        var resposta = await cliente.SendAsync(pedido);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    /* ------------------------------------------------------------- apoio */

    private async Task<List<EmpresaNaLista>> ListarEmpresasComo(Conta conta)
    {
        var cliente = _aplicacao.CreateClient();

        var entrada = await cliente.PostAsJsonAsync("/autenticacao/entrar",
            new PedidoDeEntrada(conta.Email, conta.Senha));
        entrada.EnsureSuccessStatusCode();

        var pedido = new HttpRequestMessage(HttpMethod.Get, "/empresas");
        pedido.Headers.Add("Cookie", Sessao.Cookie + "=" + ValorDoCookie(LerCookieDeSessao(entrada)));

        var resposta = await cliente.SendAsync(pedido);
        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<List<EmpresaNaLista>>())!;
    }

    private static string LerCookieDeSessao(HttpResponseMessage resposta)
    {
        Assert.True(resposta.Headers.TryGetValues("Set-Cookie", out var cookies),
            "A resposta de entrada não trouxe Set-Cookie.");
        return cookies!.Single(cookie => cookie.StartsWith(Sessao.Cookie + "=", StringComparison.Ordinal));
    }

    private static string ValorDoCookie(string cabecalho) =>
        cabecalho.Split(';')[0][(Sessao.Cookie.Length + 1)..];

    private async Task<Conta> CriarContaEmTenantNovo()
    {
        var tenantId = Guid.NewGuid();
        var razaoSocial = ("Escritório " + tenantId.ToString("N"))[..40];

        await using (var contexto = banco.Criar(tenant: null))
        {
            contexto.Tenants.Add(new Tenant { Id = tenantId, Nome = razaoSocial });
            await contexto.SaveChangesAsync();
        }

        await using (var contexto = banco.Criar(tenantId))
        {
            contexto.Empresas.Add(new Empresa
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RazaoSocial = razaoSocial,
                Cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString(),
            });
            await contexto.SaveChangesAsync();
        }

        var email = "pessoa-" + tenantId.ToString("N") + "@exemplo.com.br";
        const string senha = "SenhaDeTeste@2026";

        using (var escopo = _aplicacao.Services.CreateScope())
        {
            var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
            var resultado = await usuarios.CreateAsync(new Usuario
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                Nome = "Pessoa de teste",
                TenantId = tenantId,
            }, senha);

            Assert.True(resultado.Succeeded,
                string.Join("; ", resultado.Errors.Select(erro => erro.Description)));
        }

        return new Conta(email, senha, tenantId, razaoSocial);
    }

    private record Conta(string Email, string Senha, Guid TenantId, string RazaoSocial);
}
