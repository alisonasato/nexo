namespace Nexo.Api.Dominio;

/// <summary>
/// Quanto um contrato vale, e a partir de que competência.
///
/// <para>
/// <b>O valor do contrato não é campo.</b> Ele é a última vigência que já
/// começou — o mesmo desenho do saldo da conta, que é o inicial mais os
/// movimentos. Um valor editável em cima do anterior é o número que alguém muda
/// em março e que, em junho, ninguém mais sabe quanto era; e a mensalidade de
/// fevereiro passaria a dizer que sempre foi assim.
/// </para>
/// <para>
/// <b>A vigência começa numa competência, e não num dia.</b> Mensalidade é
/// mensal, e quem decide o valor dela é o mês a que ela se refere. Reajuste que
/// vale "a partir de abril" não deixa dúvida sobre a mensalidade de março.
/// </para>
/// </summary>
public class ValorDoContrato
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public Guid ContratoId { get; set; }
    public Contrato? Contrato { get; set; }

    public decimal Valor { get; set; }

    /// <summary>A competência a partir da qual este valor vale.</summary>
    public int VigenteDeAno { get; set; }
    public int VigenteDeMes { get; set; }

    /// <summary>
    /// Por que mudou: "Valor inicial", "IPCA de 2026", "acordo com o cliente".
    ///
    /// Exigido no reajuste porque quem olhar daqui a um ano vai perguntar de onde
    /// saiu o índice, e a resposta não pode ser memória de alguém.
    /// </summary>
    public string Motivo { get; set; } = string.Empty;

    /// <summary>O percentual que gerou este valor. Nulo quando o valor foi digitado.</summary>
    public decimal? Percentual { get; set; }

    public DateTimeOffset CriadoEm { get; set; }

    /// <summary>A competência como número comparável: ano vezes doze mais mês.</summary>
    public static int Competencia(int ano, int mes) => ano * 12 + mes;
}
