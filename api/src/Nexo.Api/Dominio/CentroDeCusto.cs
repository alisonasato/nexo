namespace Nexo.Api.Dominio;

/// <summary>
/// De quem é o dinheiro dentro do escritório: uma unidade, uma área, um projeto.
///
/// <para>
/// <b>Uma lista à parte, e não um nível a mais da árvore.</b> A categoria diz o
/// que o gasto foi, e o centro de custo diz de quem ele é. O aluguel é
/// "Ocupação" em qualquer escritório, mas pode ser da matriz ou da filial; se
/// o centro fosse um nível da árvore, cada categoria precisaria existir uma vez
/// por unidade.
/// </para>
/// <para>
/// <b>Plana, e não se apaga.</b> Escritório pequeno tem poucos centros, e o que
/// foi atribuído a um continua precisando dele depois que a unidade fecha: ele
/// fica inativo.
/// </para>
/// </summary>
public class CentroDeCusto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>Único no escritório, porque é por ele que o centro se escolhe na tela.</summary>
    public string Nome { get; set; } = string.Empty;

    public bool Ativo { get; set; } = true;

    public DateTimeOffset CriadoEm { get; set; }
    public DateTimeOffset AtualizadoEm { get; set; }
}
