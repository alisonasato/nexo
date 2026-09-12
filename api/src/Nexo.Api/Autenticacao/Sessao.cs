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
    /// O carimbo de segurança do Identity, copiado para dentro do token.
    ///
    /// <para>
    /// É o que faz trocar a senha derrubar as outras sessões. O token é
    /// autocontido e ninguém consegue apagá-lo de longe — mas o Identity troca
    /// este carimbo a cada mudança de senha, então o token antigo passa a
    /// carregar um valor que não bate mais com o banco, e cai na porta.
    /// </para>
    /// </summary>
    public const string ClaimCarimbo = "carimbo";

    /// <summary>
    /// Quando esta sessão começou, em segundos desde a época.
    ///
    /// <para>
    /// Atravessa as renovações sem ser reescrito: é ele que diz há quanto tempo
    /// a pessoa entrou, e não há quanto tempo o token atual foi assinado. Sem
    /// essa distinção, renovar reiniciaria a contagem e o teto nunca chegaria.
    /// </para>
    /// </summary>
    public const string ClaimInicio = "inicio";

    /// <summary>
    /// O token vive num cookie <c>httpOnly</c> (decisão Q29), e não no
    /// cabeçalho <c>Authorization</c>: um XSS num ERP é acesso ao dinheiro, e
    /// o que o JavaScript não alcança ele não rouba.
    /// </summary>
    public const string Cookie = "nexo_sessao";

    /// <summary>
    /// Grava o cookie de sessão.
    ///
    /// <para>
    /// Existe porque estas propriedades precisam ser idênticas nos quatro
    /// lugares que mexem no cookie — entrar, trocar a senha, renovar e sair. O
    /// navegador identifica um cookie pelo conjunto delas: divergir em uma só
    /// faz o apagar não apagar, ou a renovação criar um segundo cookie ao lado
    /// do primeiro.
    /// </para>
    /// </summary>
    public static void Gravar(HttpResponse resposta, string token, DateTimeOffset expira, bool desenvolvimento) =>
        resposta.Cookies.Append(Cookie, token, Propriedades(desenvolvimento, expira));

    public static void Apagar(HttpResponse resposta, bool desenvolvimento) =>
        resposta.Cookies.Delete(Cookie, Propriedades(desenvolvimento, expira: null));

    private static CookieOptions Propriedades(bool desenvolvimento, DateTimeOffset? expira) => new()
    {
        /* O JavaScript não alcança — nem o legítimo, nem o injetado. */
        HttpOnly = true,

        /* Em desenvolvimento a origem é http, e um cookie Secure não seria gravado. */
        Secure = !desenvolvimento,

        /*
         * Lax basta porque front e API dividem o domínio (decisão Q29), e de
         * quebra o navegador não manda o cookie em POST vindo de outro site —
         * que é proteção contra CSRF de graça.
         */
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expira,
    };
}
