using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nexo.Api.Dominio;

namespace Nexo.Api.Autenticacao;

/// <param name="relogio">
/// O tempo vem de fora para que a renovação seja testável. Sem isso, provar
/// que um token perto do fim é renovado e um recém-assinado não é exigiria
/// esperar horas de verdade.
/// </param>
public sealed class GeradorDeToken(IOptions<OpcoesDeToken> opcoes, TimeProvider relogio)
{
    private readonly OpcoesDeToken _opcoes = opcoes.Value;

    public (string Token, DateTimeOffset Expira) Gerar(Usuario usuario)
    {
        var agora = relogio.GetUtcNow();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, usuario.Email ?? string.Empty),
            new(Sessao.ClaimTenant, usuario.TenantId.ToString()),

            /*
             * O carimbo do Identity entra aqui e é conferido a cada requisição.
             * Sem ele, trocar a senha não derruba sessão nenhuma: o token é
             * autocontido e seguiria valendo até expirar, que é o contrário do
             * que quem troca a senha por suspeita de vazamento espera.
             */
            new(Sessao.ClaimCarimbo, usuario.SecurityStamp ?? string.Empty),

            /* A sessão começa agora; as renovações vão carregar esta mesma data. */
            new(Sessao.ClaimInicio, agora.ToUnixTimeSeconds().ToString()),
        };

        if (usuario.EmpresaPadraoId is { } empresa)
            claims.Add(new Claim(Sessao.ClaimEmpresa, empresa.ToString()));

        return Assinar(claims, agora);
    }

    /// <summary>
    /// Assina de novo o que já foi validado, com prazo novo.
    ///
    /// <para>
    /// As claims vêm do token que acabou de passar pela conferência de
    /// assinatura, emissor, público, prazo e carimbo de segurança — não há o
    /// que reconsultar. Reconsultar o usuário aqui custaria uma ida ao banco
    /// por renovação sem mudar nada: tenant e empresa já estavam certos quando
    /// a pessoa entrou, e trocar de empresa em tela ainda não existe.
    /// </para>
    /// <para>
    /// O que <b>não</b> se copia é o prazo e o identificador do token. O resto,
    /// inclusive quando a sessão começou, atravessa intacto.
    /// </para>
    /// </summary>
    public (string Token, DateTimeOffset Expira) Renovar(ClaimsPrincipal sessao)
    {
        var descartadas = new[]
        {
            JwtRegisteredClaimNames.Exp,
            JwtRegisteredClaimNames.Iat,
            JwtRegisteredClaimNames.Nbf,
            JwtRegisteredClaimNames.Jti,
            JwtRegisteredClaimNames.Iss,
            JwtRegisteredClaimNames.Aud,
        };

        var claims = sessao.Claims
            .Where(claim => !descartadas.Contains(claim.Type))
            .Select(claim => new Claim(claim.Type, claim.Value))
            .ToList();

        return Assinar(claims, relogio.GetUtcNow());
    }

    /// <summary>Quando esta sessão começou, se o token souber dizer.</summary>
    public static DateTimeOffset? InicioDaSessao(ClaimsPrincipal sessao)
    {
        var valor = sessao.FindFirst(Sessao.ClaimInicio)?.Value;
        return long.TryParse(valor, out var segundos)
            ? DateTimeOffset.FromUnixTimeSeconds(segundos)
            : null;
    }

    private (string, DateTimeOffset) Assinar(List<Claim> claims, DateTimeOffset agora)
    {
        var expira = agora.AddHours(_opcoes.HorasDeValidade);

        /* Identificador novo a cada assinatura: dois tokens da mesma sessão não
           são o mesmo token, e um dia isso vai importar para revogar um só. */
        claims.Add(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()));

        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opcoes.Chave));

        var token = new JwtSecurityToken(
            issuer: _opcoes.Emissor,
            audience: _opcoes.Publico,
            claims: claims,
            expires: expira.UtcDateTime,
            signingCredentials: new SigningCredentials(chave, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expira);
    }
}
