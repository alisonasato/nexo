using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nexo.Api.Dominio;

namespace Nexo.Api.Autenticacao;

public sealed class GeradorDeToken(IOptions<OpcoesDeToken> opcoes)
{
    private readonly OpcoesDeToken _opcoes = opcoes.Value;

    public (string Token, DateTimeOffset Expira) Gerar(Usuario usuario)
    {
        var expira = DateTimeOffset.UtcNow.AddHours(_opcoes.HorasDeValidade);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, usuario.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(Sessao.ClaimTenant, usuario.TenantId.ToString()),
        };

        if (usuario.EmpresaPadraoId is { } empresa)
            claims.Add(new Claim(Sessao.ClaimEmpresa, empresa.ToString()));

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
