namespace Nexo.Api.Dominio;

public enum SituacaoRecebivel
{
    Aberto = 1,
    Pago = 2,
    Cancelado = 3,
}

/// <summary>
/// Uma cobrança devida ao escritório: a mensalidade de um contrato numa
/// competência, ou um valor avulso.
///
/// <para>
/// <b>Competência é o par ano/mês a que o valor se refere</b>, e não a data em
/// que ele vence — os dois quase nunca coincidem. A mensalidade de janeiro
/// costuma vencer em fevereiro, e é por competência que o escritório fecha o
/// mês.
/// </para>
/// <para>
/// O par <c>ContratoId + Competencia</c> é único. É isso, e não uma checagem
/// no código, que torna "gerar mensalidades" seguro de rodar duas vezes: se o
/// escritório clicar de novo, ou duas pessoas clicarem ao mesmo tempo, o banco
/// recusa a segunda. Cobrar o cliente em dobro é o erro que não se conserta
/// com pedido de desculpas.
/// </para>
/// </summary>
public class Recebivel
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public Guid ClienteId { get; set; }
    public Cliente? Cliente { get; set; }

    /// <summary>Nulo quando o valor é avulso, sem contrato por trás.</summary>
    public Guid? ContratoId { get; set; }
    public Contrato? Contrato { get; set; }

    public int CompetenciaAno { get; set; }
    public int CompetenciaMes { get; set; }

    public string Descricao { get; set; } = string.Empty;

    public decimal Valor { get; set; }
    public DateOnly Vencimento { get; set; }

    public SituacaoRecebivel Situacao { get; set; } = SituacaoRecebivel.Aberto;

    /* ------------------------------------------------------------ baixa */

    /// <summary>
    /// Quanto entrou de fato. Pode diferir do valor: desconto combinado,
    /// pagamento parcial, juros de atraso.
    /// </summary>
    public decimal? ValorPago { get; set; }

    /// <summary>Data em que o dinheiro entrou, não a data em que foi registrado.</summary>
    public DateOnly? PagoEm { get; set; }

    /// <summary>
    /// Como a baixa aconteceu: à mão ou por retorno do meio de cobrança.
    /// Guardado porque, quando o valor não bate, a primeira pergunta é sempre
    /// "quem deu essa baixa".
    /// </summary>
    public string OrigemDaBaixa { get; set; } = string.Empty;

    public DateTimeOffset CriadoEm { get; set; }
    public DateTimeOffset AtualizadoEm { get; set; }

    public bool EstaVencido(DateOnly hoje) =>
        Situacao == SituacaoRecebivel.Aberto && Vencimento < hoje;
}

public static class OrigensDeBaixa
{
    public const string Manual = "manual";
    public const string Cobranca = "cobranca";
}
