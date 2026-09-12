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
    /// <summary>
    /// Onde uma requisição sem sessão deixa o tenant que ela mesma apurou.
    ///
    /// Existe por causa do webhook do PSP: ele chega sem token, porque quem
    /// chama é o Asaas, e mesmo assim precisa enxergar as linhas de um tenant.
    /// </summary>
    public const string ChaveDoItem = "nexo.tenant";

    public Guid? TenantAtual
    {
        get
        {
            var contexto = acessor.HttpContext;
            if (contexto is null) return null;

            if (Guid.TryParse(contexto.User.FindFirst(Sessao.ClaimTenant)?.Value, out var daClaim))
                return daClaim;

            /*
             * O caminho sem sessão, e ele vem depois da claim de propósito.
             *
             * Quem chega por aqui é o webhook do PSP, que se autenticou pelo
             * token combinado e apurou o tenant a partir de um dado que nós
             * mesmos escrevemos na cobrança. A ordem importa: numa requisição
             * com sessão, quem manda é a claim — nada gravado no meio do
             * caminho pode trocar o tenant de quem está autenticado.
             */
            return contexto.Items.TryGetValue(ChaveDoItem, out var apurado) && apurado is Guid id
                ? id
                : null;
        }
    }

    /// <summary>
    /// Fixa o tenant de uma requisição que não tem sessão para carregá-lo.
    ///
    /// <b>Precisa acontecer antes do primeiro toque no banco.</b> O interceptor
    /// grava <c>app.tenant_id</c> quando a conexão abre, e uma conexão aberta
    /// antes disto abriria sem tenant nenhum — devolvendo lista vazia, que é
    /// pior que erro porque parece resposta.
    /// </summary>
    public static void Fixar(HttpContext contexto, Guid tenant) =>
        contexto.Items[ChaveDoItem] = tenant;
}
