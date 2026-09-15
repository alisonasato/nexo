namespace Nexo.Api.Dominio;

/// <summary>De quantos em quantos meses a recorrência se repete. O número é o intervalo.</summary>
public enum FrequenciaDeRecorrencia
{
    Mensal = 1,
    Trimestral = 3,
    Semestral = 6,
    Anual = 12,
}

/// <summary>
/// Um lançamento que se repete sem contrato por trás: o aluguel do escritório,
/// a licença anual do sistema, o serviço que o cliente paga todo trimestre.
///
/// <para>
/// <b>Cadastro próprio, e não contrato com natureza.</b> O contrato é o acordo
/// com o cliente: tem código, cobrança pelo PSP e o total mensal que responde
/// quanto o escritório fatura. O aluguel ali faria esse total somar despesa.
/// </para>
/// <para>
/// <b>Gera sob demanda, por competência</b>, com a mesma trava dos contratos: o
/// banco recusa um segundo lançamento da mesma recorrência na mesma competência.
/// </para>
/// <para>
/// <b>A frequência conta a partir do mês de início.</b> Trimestral começando em
/// fevereiro gera em fevereiro, maio, agosto e novembro.
/// </para>
/// </summary>
public class Recorrencia
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>
    /// A receber ou a pagar. <b>Não muda depois de criada:</b> trocar a natureza
    /// de uma recorrência que já gerou lançamentos deixaria os de antes de um
    /// lado e os de depois do outro, sob o mesmo nome.
    /// </summary>
    public required NaturezaLancamento Natureza { get; set; }

    /// <summary>Cliente quando é a receber, fornecedor quando é a pagar.</summary>
    public Guid PessoaId { get; set; }
    public Pessoa? Pessoa { get; set; }

    public string Descricao { get; set; } = string.Empty;

    /// <summary>O valor de cada lançamento gerado, e não da soma do ano.</summary>
    public decimal Valor { get; set; }

    public FrequenciaDeRecorrencia Frequencia { get; set; } = FrequenciaDeRecorrencia.Mensal;

    /// <summary>Dia do mês em que vence, de 1 a 31. Nos meses mais curtos, o último dia.</summary>
    public int DiaDeVencimento { get; set; } = 10;

    /// <summary>A primeira competência é o mês desta data, e é dele que a frequência conta.</summary>
    public DateOnly InicioEm { get; set; }

    /// <summary>Nulo enquanto não tem prazo para acabar.</summary>
    public DateOnly? FimEm { get; set; }

    /// <summary>
    /// Inativa deixa de gerar, e fica. Os lançamentos que ela já gerou continuam
    /// apontando para ela.
    /// </summary>
    public bool Ativa { get; set; } = true;

    /// <summary>A classificação que os lançamentos gerados levam. Opcional.</summary>
    public Guid? CategoriaId { get; set; }
    public Categoria? Categoria { get; set; }

    public Guid? CentroDeCustoId { get; set; }
    public CentroDeCusto? CentroDeCusto { get; set; }

    public DateTimeOffset CriadoEm { get; set; }
    public DateTimeOffset AtualizadoEm { get; set; }

    /// <summary>
    /// Se esta recorrência gera lançamento na competência informada: ativa,
    /// dentro da vigência, e num mês que a frequência não pula.
    /// </summary>
    public bool VigenteEm(int ano, int mes)
    {
        if (!Ativa) return false;

        var primeiroDia = new DateOnly(ano, mes, 1);
        var ultimoDia = primeiroDia.AddMonths(1).AddDays(-1);

        /* Começa depois do fim do mês: ainda não vale. */
        if (InicioEm > ultimoDia) return false;

        /* Terminou antes do começo do mês: já não vale. */
        if (FimEm is { } fim && fim < primeiroDia) return false;

        var mesesDesdeOInicio = (ano * 12 + mes) - (InicioEm.Year * 12 + InicioEm.Month);
        return mesesDesdeOInicio % (int)Frequencia == 0;
    }

    /// <summary>
    /// A data de vencimento na competência informada. O dia é preso ao último do
    /// mês: dia 31 vence em 30 de abril e em 28 de fevereiro, sem escorregar para o
    /// mês seguinte.
    /// </summary>
    public DateOnly VencimentoEm(int ano, int mes) =>
        new(ano, mes, Math.Clamp(DiaDeVencimento, 1, DateTime.DaysInMonth(ano, mes)));
}
