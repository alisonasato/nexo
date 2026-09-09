using Microsoft.AspNetCore.Identity;

namespace Nexo.Api.Dominio;

/// <summary>
/// Quem entra no sistema. Pertence a exatamente um tenant.
///
/// O <c>TenantId</c> daqui é a origem de tudo: ele vira claim no token, o token
/// vira <c>app.tenant_id</c> na sessão do Postgres, e é isso que as políticas
/// de RLS leem. Errar este campo é errar o isolamento inteiro.
///
/// <para>
/// <b>Esta tabela não tem RLS</b>, e é uma exceção deliberada. O login procura
/// o usuário pelo e-mail antes de existir qualquer tenant conhecido — não há
/// como uma política de linha proteger uma busca que precisa acontecer antes de
/// a política ter o que comparar. Qualquer política do tipo "quando não há
/// tenant, libera tudo" não protegeria nada, só pareceria proteger.
/// </para>
/// <para>
/// A consequência, que é uma regra e não uma observação: <b>toda consulta a
/// usuários fora do caminho do login filtra por tenant explicitamente.</b> Aqui
/// o <c>WHERE</c> esquecido volta a ser perigoso, porque o banco não segura.
/// </para>
/// </summary>
public class Usuario : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public required string Nome { get; set; }

    /// <summary>
    /// Em qual estabelecimento a pessoa trabalha por padrão. Nulo enquanto o
    /// tenant tiver uma empresa só. Trocar de empresa em tela é assunto de
    /// outro passo.
    /// </summary>
    public Guid? EmpresaPadraoId { get; set; }
}
