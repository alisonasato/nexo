namespace Nexo.Api.Dominio;

/// <summary>
/// O acordo que trocou um título em aberto por parcelas novas.
///
/// <para>
/// <b>Renegociar não edita título.</b> O original vira renegociado e fica como
/// histórico do que era devido; as parcelas novas são lançamentos próprios que
/// apontam para ele. Este registro guarda o que não cabe em nenhum dos dois: o
/// que foi acrescentado, o que foi abatido e por quê.
/// </para>
/// <para>
/// Juros, multa e desconto são <b>valores digitados</b>, e não calculados por
/// taxa. Juros e multa automáticos sobre o vencido dependem de uma taxa que
/// ainda é decisão em aberto no DEPOIS.md; aqui quem negocia diz quanto.
/// </para>
/// </summary>
public class Renegociacao
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>O título que foi substituído. Um título se renegocia uma vez só.</summary>
    public Guid OrigemId { get; set; }
    public Lancamento? Origem { get; set; }

    public decimal Juros { get; set; }
    public decimal Multa { get; set; }
    public decimal Desconto { get; set; }

    public string Motivo { get; set; } = string.Empty;

    public DateTimeOffset CriadoEm { get; set; }
}
