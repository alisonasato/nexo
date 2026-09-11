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
/// O par <c>ContratoId + Competencia</c> é único <b>entre os não cancelados</b>.
/// É isso, e não uma checagem no código, que torna "gerar mensalidades" seguro
/// de rodar duas vezes: se o escritório clicar de novo, ou duas pessoas
/// clicarem ao mesmo tempo, o banco recusa a segunda. Cobrar o cliente em dobro
/// é o erro que não se conserta com pedido de desculpas.
/// </para>
/// <para>
/// O recorte "entre os não cancelados" é o que permite corrigir. Um índice
/// cego à situação transformaria erro comum em erro permanente: valor do
/// contrato errado, mensalidades geradas, contrato corrigido — e nada a fazer,
/// porque a competência ficaria ocupada por um recebível errado para sempre.
/// Cancelado sai da conta e libera a competência, sem sumir do histórico.
/// </para>
/// </summary>
public class Recebivel
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>A pessoa com quem o acordo existe. Ela carrega o papel de cliente. </summary>
    public Guid PessoaId { get; set; }
    public Pessoa? Pessoa { get; set; }

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

    /// <summary>
    /// Por que foi cancelado. Vazio enquanto não for.
    ///
    /// Cancelar é sumir um valor da conta do escritório, e quem olhar depois
    /// vai perguntar por quê. Sem campo para a resposta, a explicação vira
    /// memória de alguém.
    /// </summary>
    public string MotivoDoCancelamento { get; set; } = string.Empty;

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
