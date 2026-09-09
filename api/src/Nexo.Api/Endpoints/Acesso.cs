using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Autenticacao;
using Nexo.Api.Dominio;

namespace Nexo.Api.Endpoints;

public static class Acesso
{
    public static IEndpointRouteBuilder MapAcesso(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/autenticacao").WithTags("Acesso");

        /*
         * Os `Produces` não são enfeite: como estes handlers devolvem
         * `IResult`, nada é inferido, e sem eles o documento OpenAPI sai sem
         * esquema de resposta. O cliente TypeScript gerado então tipa o retorno
         * como `never` — e o front só descobre isso quando tenta usar o dado.
         */
        grupo.MapPost("/entrar", Entrar)
            .WithName("Entrar")
            .WithSummary("Abre a sessão")
            .Produces<SessaoAberta>(StatusCodes.Status200OK)
            .Produces<FalhaDeEntrada>(StatusCodes.Status401Unauthorized)
            .Produces<FalhaDeEntrada>(StatusCodes.Status423Locked)
            .AllowAnonymous();

        grupo.MapPost("/sair", Sair)
            .WithName("Sair")
            .WithSummary("Encerra a sessão")
            .Produces(StatusCodes.Status204NoContent)
            .AllowAnonymous();

        grupo.MapGet("/eu", Eu)
            .WithName("ConsultarSessao")
            .WithSummary("Quem está na sessão")
            .Produces<SessaoAtual>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        return rotas;
    }

    private static async Task<IResult> Entrar(
        [FromBody] PedidoDeEntrada pedido,
        UserManager<Usuario> usuarios,
        SignInManager<Usuario> entradas,
        GeradorDeToken gerador,
        IHostEnvironment ambiente,
        HttpContext http)
    {
        var usuario = await usuarios.FindByEmailAsync(pedido.Email);

        /*
         * Uma resposta só para os dois casos — e-mail que não existe e senha
         * errada. Distinguir os dois entrega a lista de quem tem conta.
         */
        if (usuario is null)
            return Results.Json(new FalhaDeEntrada("E-mail ou senha incorretos."), statusCode: 401);

        var resultado = await entradas.CheckPasswordSignInAsync(usuario, pedido.Senha, lockoutOnFailure: true);

        if (resultado.IsLockedOut)
            return Results.Json(
                new FalhaDeEntrada("Conta bloqueada por tentativas seguidas. Tente de novo em alguns minutos."),
                statusCode: 423);

        if (!resultado.Succeeded)
            return Results.Json(new FalhaDeEntrada("E-mail ou senha incorretos."), statusCode: 401);

        var (token, expira) = gerador.Gerar(usuario);

        http.Response.Cookies.Append(Sessao.Cookie, token, new CookieOptions
        {
            /* O JavaScript não alcança — nem o legítimo, nem o injetado. */
            HttpOnly = true,

            /* Em desenvolvimento a origem é http, e um cookie Secure não seria gravado. */
            Secure = !ambiente.IsDevelopment(),

            /*
             * Lax basta porque front e API dividem o domínio (decisão Q29), e
             * de quebra o navegador não manda o cookie em POST vindo de outro
             * site — que é proteção contra CSRF de graça.
             */
            SameSite = SameSiteMode.Lax,
            Expires = expira,
            Path = "/",
        });

        return Results.Ok(new SessaoAberta(usuario.Nome, usuario.Email ?? string.Empty, usuario.TenantId, expira));
    }

    private static IResult Sair(HttpContext http, IHostEnvironment ambiente)
    {
        /*
         * Apagar exige as mesmas propriedades usadas para gravar; sem elas o
         * navegador entende que é outro cookie e mantém o original.
         */
        http.Response.Cookies.Delete(Sessao.Cookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = !ambiente.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });

        return Results.NoContent();
    }

    private static IResult Eu(ClaimsPrincipal quem)
    {
        var tenant = quem.FindFirst(Sessao.ClaimTenant)?.Value ?? string.Empty;
        var email = quem.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)?.Value ?? string.Empty;

        return Results.Ok(new SessaoAtual(email, Guid.Parse(tenant), quem.FindFirst(Sessao.ClaimEmpresa)?.Value));
    }
}

public record PedidoDeEntrada(string Email, string Senha);
public record SessaoAberta(string Nome, string Email, Guid TenantId, DateTimeOffset Expira);
public record SessaoAtual(string Email, Guid TenantId, string? EmpresaId);
public record FalhaDeEntrada(string Mensagem);
