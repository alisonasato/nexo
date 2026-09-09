namespace Nexo.Api.Dominio;

/// <summary>
/// O cliente do ERP — o escritório que assina o Nexo.
///
/// É o registro de tenants, e por isso **não** tem RLS: é a tabela que a
/// autenticação precisa consultar antes de saber qual é o tenant da requisição.
/// Toda tabela de negócio, essa sim, carrega <c>TenantId</c> e política de
/// linha (decisão Q22).
/// </summary>
public class Tenant
{
    public Guid Id { get; set; }
    public required string Nome { get; set; }
    public DateTimeOffset CriadoEm { get; set; }

    public ICollection<Empresa> Empresas { get; set; } = [];
}
