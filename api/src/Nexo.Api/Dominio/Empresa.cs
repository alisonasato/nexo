namespace Nexo.Api.Dominio;

/// <summary>
/// O estabelecimento: uma matriz, ou uma filial de uma matriz.
///
/// Este é o segundo nível da decisão Q22, e a razão de ele existir desde a
/// primeira migração: o Cuca fundiu tenant e empresa em um único
/// <c>EmpresaId</c>, e é por isso que ele virou instalação por cliente em vez
/// de produto. Separar agora custa uma coluna.
/// </summary>
public class Empresa
{
    public Guid Id { get; set; }

    /// <summary>Quem é o dono da linha. A política de RLS compara este campo.</summary>
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public required string RazaoSocial { get; set; }
    public string? NomeFantasia { get; set; }
    public required string Cnpj { get; set; }

    /// <summary>Nulo quando a empresa é a matriz; preenchido quando é filial.</summary>
    public Guid? MatrizId { get; set; }
    public Empresa? Matriz { get; set; }
    public ICollection<Empresa> Filiais { get; set; } = [];

    public DateTimeOffset CriadoEm { get; set; }
}
