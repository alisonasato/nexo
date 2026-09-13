namespace Nexo.Api.Dominio;

/// <summary>O que a conta é. Dinheiro em caixa não tem banco, agência nem número.</summary>
public enum TipoConta
{
    Corrente = 1,
    Poupanca = 2,

    /// <summary>Conta de pagamento, como a do PSP: recebe boleto e Pix e transfere para o banco.</summary>
    Pagamento = 3,

    /// <summary>O caixa físico do escritório.</summary>
    Dinheiro = 4,
}

/// <summary>
/// Onde o dinheiro do escritório fica.
///
/// <para>
/// <b>O saldo não é campo.</b> A conta guarda o saldo inicial e o dia em que ele
/// valia; o saldo de qualquer outro dia é esse número mais o que entrou e saiu
/// desde então. Um saldo editável seria o número que alguém ajusta para bater
/// com o extrato, e aí nunca mais se sabe por que não batia.
/// </para>
/// <para>
/// <b>O saldo inicial vale no começo do dia.</b> O que entra ou sai na própria
/// data conta depois dele. Assim a conta cadastrada hoje, com o saldo de hoje,
/// já recebe as baixas de hoje sem contar nenhuma duas vezes.
/// </para>
/// </summary>
public class ContaBancaria
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>
    /// Como o escritório chama a conta: "Itaú PJ", "Asaas", "Caixa da recepção".
    /// Único no escritório, porque é por ele que a conta se escolhe na tela.
    /// </summary>
    public string Nome { get; set; } = string.Empty;

    public TipoConta Tipo { get; set; }

    /// <summary>Vazios quando não se aplicam, como no caixa em dinheiro.</summary>
    public string Banco { get; set; } = string.Empty;
    public string Agencia { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;

    /// <summary>Pode ser negativo: conta que começa no cheque especial.</summary>
    public decimal SaldoInicial { get; set; }
    public DateOnly SaldoInicialEm { get; set; }

    /// <summary>
    /// Conta encerrada fica, inativa. O dinheiro que passou por ela continua
    /// precisando dela para ter onde ter passado.
    /// </summary>
    public bool Ativa { get; set; } = true;

    public DateTimeOffset CriadoEm { get; set; }
    public DateTimeOffset AtualizadoEm { get; set; }
}
