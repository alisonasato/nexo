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

        grupo.MapPost("/trocar-senha", TrocarSenha)
            .WithName("TrocarSenha")
            .WithSummary("Troca a senha de quem está na sessão")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization();

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

    /// <summary>
    /// Troca a senha de quem está na sessão.
    ///
    /// <para>
    /// Exige a senha atual mesmo com sessão aberta. Sessão aberta prova que
    /// alguém entrou, não que continua sendo a mesma pessoa — computador
    /// destravado num escritório é o caso comum, e sem a senha atual bastaria
    /// sentar na cadeira para tomar a conta.
    /// </para>
    /// <para>
    /// <b>O token antigo continua valendo até expirar.</b> Ele é autocontido:
    /// nada no banco o invalida. Quem trocou a senha ganha um cookie novo aqui,
    /// mas uma sessão aberta em outro navegador segue funcionando por até oito
    /// horas. Se isso passar a importar — e passa, no dia em que a troca for
    /// por suspeita de vazamento — a resposta é validar o carimbo de segurança
    /// do Identity a cada requisição, ao custo de uma consulta.
    /// </para>
    /// </summary>
    private static async Task<IResult> TrocarSenha(
        [FromBody] PedidoDeTrocaDeSenha pedido,
        UserManager<Usuario> usuarios,
        GeradorDeToken gerador,
        IHostEnvironment ambiente,
        ClaimsPrincipal quem,
        HttpContext http)
    {
        var usuario = await usuarios.GetUserAsync(quem);

        /*
         * Token válido para um usuário que não existe mais: sessão aberta antes
         * de o cadastro sumir. Recusar sem cerimônia.
         */
        if (usuario is null) return Results.Unauthorized();

        if (string.IsNullOrEmpty(pedido.SenhaNova))
        {
            return Recusa("senhaNova", "Senha inválida",
                "A nova senha não foi informada.",
                "Escolha uma senha que você não use em outro lugar.");
        }

        if (pedido.SenhaAtual == pedido.SenhaNova)
        {
            return Recusa("senhaNova", "Senha inválida",
                "A nova senha é igual à atual.",
                "Escolha uma senha diferente da que está em uso.");
        }

        var resultado = await usuarios.ChangePasswordAsync(usuario, pedido.SenhaAtual, pedido.SenhaNova);

        if (!resultado.Succeeded)
        {
            /*
             * As mensagens vêm do Identity, traduzidas em DescritorDeErros. O
             * campo é escolhido pelo código do erro: senha atual errada aponta
             * para o campo da senha atual, e regra de força aponta para a nova
             * — senão o erro aparece embaixo do campo errado.
             */
            var problemas = resultado.Errors.Select(erro => new Problema(
                erro.Code == "PasswordMismatch" ? "senhaAtual" : "senhaNova",
                "Não foi possível trocar a senha",
                erro.Description,
                erro.Code == "PasswordMismatch"
                    ? "Digite a senha que você usa hoje para entrar."
                    : "Ajuste a nova senha conforme a mensagem acima.")).ToList();

            return Results.Json(new RespostaComProblemas(problemas), statusCode: 422);
        }

        /*
         * Cookie novo na mesma resposta. Sem isto a pessoa troca a senha e
         * segue com o token antigo — que funciona, mas some no fim do prazo
         * sem que ela entenda por quê.
         */
        var (token, expira) = gerador.Gerar(usuario);

        http.Response.Cookies.Append(Sessao.Cookie, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = !ambiente.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Expires = expira,
            Path = "/",
        });

        return Results.NoContent();
    }

    private static IResult Recusa(string campo, string titulo, string descricao, string sugestao) =>
        Results.Json(
            new RespostaComProblemas([new Problema(campo, titulo, descricao, sugestao)]),
            statusCode: 422);

    private static IResult Eu(ClaimsPrincipal quem)
    {
        var tenant = quem.FindFirst(Sessao.ClaimTenant)?.Value ?? string.Empty;
        var email = quem.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)?.Value ?? string.Empty;

        return Results.Ok(new SessaoAtual(email, Guid.Parse(tenant), quem.FindFirst(Sessao.ClaimEmpresa)?.Value));
    }
}

public record PedidoDeEntrada(string Email, string Senha);
public record PedidoDeTrocaDeSenha(string SenhaAtual, string SenhaNova);
public record SessaoAberta(string Nome, string Email, Guid TenantId, DateTimeOffset Expira);
public record SessaoAtual(string Email, Guid TenantId, string? EmpresaId);
public record FalhaDeEntrada(string Mensagem);
