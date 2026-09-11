namespace Nexo.Api.Dominio;

public enum SituacaoContrato
{
    Ativo = 1,
    Suspenso = 2,
    Encerrado = 3,
}

/// <summary>
/// O acordo recorrente: quanto este cliente paga, todo mês, e por quê.
///
/// É daqui que sai a mensalidade. Um cliente pode ter mais de um contrato — o
/// honorário contábil e a folha, por exemplo —, e cada um gera o seu recebível,
/// porque um pode ser suspenso sem o outro.
/// </summary>
public class Contrato
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>A pessoa com quem o acordo existe. Ela carrega o papel de cliente. </summary>
    public Guid PessoaId { get; set; }
    public Pessoa? Pessoa { get; set; }

    /// <summary>Código sequencial visível, no formato C0001.</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Descricao { get; set; } = string.Empty;

    public decimal Valor { get; set; }

    /// <summary>
    /// Dia do mês em que vence, de 1 a 31.
    ///
    /// Guarda-se o dia, não a data: a data de cada mensalidade é derivada dele
    /// com a competência. Em fevereiro, dia 31 vira o último dia do mês — o
    /// ajuste é feito na geração, não no cadastro, para que ninguém precise
    /// cadastrar "dia 31, exceto quando não existir".
    /// </summary>
    public int DiaDeVencimento { get; set; } = 10;

    public DateOnly InicioDaVigencia { get; set; }

    /// <summary>Nulo enquanto o contrato não tem prazo para acabar.</summary>
    public DateOnly? FimDaVigencia { get; set; }

    public SituacaoContrato Situacao { get; set; } = SituacaoContrato.Ativo;

    public string Observacoes { get; set; } = string.Empty;

    public DateTimeOffset CriadoEm { get; set; }
    public DateTimeOffset AtualizadoEm { get; set; }

    /// <summary>
    /// Se este contrato deve gerar mensalidade na competência informada.
    ///
    /// Fica no domínio, e não na consulta, porque é a regra que decide se o
    /// escritório vai cobrar alguém — e regra de cobrança escrita dentro de um
    /// <c>Where</c> é regra que ninguém acha depois.
    /// </summary>
    public bool VigenteEm(int ano, int mes)
    {
        if (Situacao != SituacaoContrato.Ativo) return false;

        var primeiroDia = new DateOnly(ano, mes, 1);
        var ultimoDia = primeiroDia.AddMonths(1).AddDays(-1);

        /* Começou depois do fim do mês: ainda não vale. */
        if (InicioDaVigencia > ultimoDia) return false;

        /* Terminou antes do começo do mês: já não vale. */
        if (FimDaVigencia is { } fim && fim < primeiroDia) return false;

        return true;
    }

    /// <summary>
    /// A data de vencimento na competência informada.
    ///
    /// O dia é preso ao último dia do mês: contrato com vencimento no dia 31
    /// vence em 28 de fevereiro, não estoura nem escorrega para março.
    /// </summary>
    public DateOnly VencimentoEm(int ano, int mes)
    {
        var diasNoMes = DateTime.DaysInMonth(ano, mes);
        return new DateOnly(ano, mes, Math.Clamp(DiaDeVencimento, 1, diasNoMes));
    }
}
