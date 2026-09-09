namespace Nexo.Api.Dominio;

public enum RegimeTributario
{
    Mei = 1,
    SimplesNacional = 2,
    LucroPresumido = 3,
    LucroReal = 4,
    TerceiroSetor = 5,
    PessoaFisica = 6,
}

/// <summary>
/// O vínculo que faz uma pessoa ser cliente do escritório.
///
/// É entidade própria, e não uma coluna em <see cref="Pessoa"/>, porque a
/// mesma pessoa pode ser cliente e fornecedora ao mesmo tempo — e vai ser, o
/// contador do escritório costuma ser cliente de si mesmo. Um cadastro só,
/// vários vínculos.
///
/// O que <b>não</b> mora aqui: valor, vencimento e vigência. Isso é do
/// contrato. Guardar nos dois lugares criaria duas respostas para "quanto este
/// cliente paga por mês", e uma delas estaria errada.
/// </summary>
public class Cliente
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public Guid PessoaId { get; set; }
    public Pessoa? Pessoa { get; set; }

    /// <summary>Código sequencial visível, no formato C-0001.</summary>
    public string Codigo { get; set; } = string.Empty;

    public RegimeTributario RegimeTributario { get; set; } = RegimeTributario.SimplesNacional;

    /// <summary>Quem responde por este cliente dentro do escritório.</summary>
    public string Responsavel { get; set; } = string.Empty;

    public string Observacoes { get; set; } = string.Empty;

    public bool Ativo { get; set; } = true;
    public DateTimeOffset CriadoEm { get; set; }
}
