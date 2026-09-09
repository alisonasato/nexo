using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Api.Autenticacao;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>Um tenant com uma empresa e um usuário, prontos para entrar.</summary>
public record ContaDeTestes(string Email, string Senha, Guid TenantId, string RazaoSocial);

public static class Contas
{
    public const string SenhaPadrao = "SenhaDeTeste@2026";

    /// <summary>
    /// Monta um tenant novo do zero. Cada teste chama isto e ganha um mundo
    /// só seu — por isso nenhum teste precisa limpar o banco, e nenhum
    /// atrapalha o outro.
    /// </summary>
    public static async Task<ContaDeTestes> Criar(
        BancoDeTestes banco,
        AplicacaoDeTestes aplicacao,
        bool comEmpresa = true)
    {
        var tenantId = Guid.NewGuid();
        var razaoSocial = ("Escritório " + tenantId.ToString("N"))[..40];

        /* A tabela de tenants não tem RLS: é o registro que o login consulta. */
        await using (var contexto = banco.Criar(tenant: null))
        {
            contexto.Tenants.Add(new Tenant { Id = tenantId, Nome = razaoSocial });
            await contexto.SaveChangesAsync();
        }

        if (comEmpresa)
        {
            /* Já a empresa só entra falando como o próprio tenant: é o WITH CHECK. */
            await using var contexto = banco.Criar(tenantId);
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

        using (var escopo = aplicacao.Services.CreateScope())
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
            }, SenhaPadrao);

            Assert.True(resultado.Succeeded,
                string.Join("; ", resultado.Errors.Select(erro => erro.Description)));
        }

        return new ContaDeTestes(email, SenhaPadrao, tenantId, razaoSocial);
    }

    /// <summary>Um cliente HTTP já com o cookie de sessão da conta informada.</summary>
    public static async Task<HttpClient> Entrar(AplicacaoDeTestes aplicacao, ContaDeTestes conta)
    {
        var cliente = aplicacao.CreateClient();

        var resposta = await cliente.PostAsJsonAsync("/autenticacao/entrar",
            new PedidoDeEntrada(conta.Email, conta.Senha));
        resposta.EnsureSuccessStatusCode();

        /*
         * O HttpClient da fábrica não guarda cookies, então o valor é copiado
         * à mão para o cabeçalho. Dá um passo a mais, e em troca o teste
         * enxerga o cookie que a API mandou de verdade.
         */
        cliente.DefaultRequestHeaders.Add("Cookie", Sessao.Cookie + "=" + ValorDoCookie(resposta));
        return cliente;
    }

    public static string CabecalhoDeCookie(HttpResponseMessage resposta)
    {
        Assert.True(resposta.Headers.TryGetValues("Set-Cookie", out var cookies),
            "A resposta de entrada não trouxe Set-Cookie.");
        return cookies!.Single(cookie => cookie.StartsWith(Sessao.Cookie + "=", StringComparison.Ordinal));
    }

    public static string ValorDoCookie(HttpResponseMessage resposta) =>
        CabecalhoDeCookie(resposta).Split(';')[0][(Sessao.Cookie.Length + 1)..];
}
