namespace Nexo.Api.Autenticacao;

/// <summary>
/// Os nomes que atravessam a fronteira entre a API, o token e o Postgres.
///
/// Estão juntos aqui de propósito: <see cref="ClaimTenant"/> é escrito na
/// geração do token, lido pelo contexto de tenant e, no fim, gravado em
/// <c>app.tenant_id</c> pela política de RLS. Três lugares, um nome — errar a
/// grafia em um deles é um vazamento silencioso.
/// </summary>
public static class Sessao
{
    public const string ClaimTenant = "tenant_id";
    public const string ClaimEmpresa = "empresa_id";

    /// <summary>
    /// O token vive num cookie <c>httpOnly</c> (decisão Q29), e não no
    /// cabeçalho <c>Authorization</c>: um XSS num ERP é acesso ao dinheiro, e
    /// o que o JavaScript não alcança ele não rouba.
    /// </summary>
    public const string Cookie = "nexo_sessao";
}
