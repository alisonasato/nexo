using Nexo.Api.Autenticacao;

namespace Nexo.Api.Dados;

/// <summary>
/// O tenant da requisição, lido da claim do token (decisão Q26).
///
/// A leitura é preguiçosa de propósito: quem pergunta é o interceptor, no
/// momento em que a conexão abre — depois de a autenticação já ter rodado. Se
/// alguma conexão abrir antes disso, ou se a requisição for anônima, o valor é
/// nulo, e nulo significa nenhuma linha. A falha continua fechada.
/// </summary>
public sealed class ContextoDeTenantHttp(IHttpContextAccessor acessor) : IContextoDeTenant
{
    public Guid? TenantAtual =>
        Guid.TryParse(acessor.HttpContext?.User.FindFirst(Sessao.ClaimTenant)?.Value, out var id)
            ? id
            : null;
}
