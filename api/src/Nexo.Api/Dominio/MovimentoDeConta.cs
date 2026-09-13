namespace Nexo.Api.Dominio;

/// <summary>De onde veio um movimento. Guardado porque "por que o saldo mudou" é a primeira pergunta de quem concilia.</summary>
public static class OrigensDeMovimento
{
    /// <summary>A baixa viva de um lançamento. Existe no máximo uma por lançamento.</summary>
    public const string Baixa = "baixa";

    /// <summary>Uma baixa que o PSP estornou. Fica, porque o dinheiro entrou de fato; a devolução ao lado a anula.</summary>
    public const string BaixaEstornada = "baixa-estornada";

    /// <summary>A saída do dinheiro que o PSP devolveu ao cliente.</summary>
    public const string EstornoDoPsp = "estorno-psp";

    /// <summary>A tarifa que o banco cobrou. Sai.</summary>
    public const string Tarifa = "tarifa";

    /// <summary>O rendimento da conta ou da aplicação. Entra.</summary>
    public const string Rendimento = "rendimento";

    /// <summary>Dinheiro que entrou e não é lançamento de ninguém, como o aporte do sócio.</summary>
    public const string EntradaAvulsa = "entrada-avulsa";

    /// <summary>Dinheiro que saiu e não é lançamento de ninguém, como a retirada do sócio.</summary>
    public const string SaidaAvulsa = "saida-avulsa";

    /// <summary>Uma ponta de uma transferência entre contas do escritório.</summary>
    public const string Transferencia = "transferencia";

    /// <summary>
    /// As origens que se apagam pelo extrato. As de baixa não entram aqui: quem
    /// as desfaz é o estorno, que também reabre o lançamento.
    /// </summary>
    public static readonly string[] Avulsas = [Tarifa, Rendimento, EntradaAvulsa, SaidaAvulsa, Transferencia];
}

/// <summary>
/// Dinheiro que entrou numa conta ou saiu dela.
///
/// <para>
/// <b>O sinal diz o lado:</b> positivo entra, negativo sai. O saldo de uma conta
/// é o saldo inicial mais a soma dos movimentos, e mais nada.
/// </para>
/// <para>
/// <b>O estorno feito aqui apaga; o estorno do PSP compensa.</b> O primeiro
/// corrige um registro que não devia existir, e o extrato do banco nunca teve
/// aquele dinheiro. O segundo devolve dinheiro que entrou de verdade, e o
/// extrato do PSP mostra as duas linhas: a entrada e a devolução.
/// </para>
/// </summary>
public class MovimentoDeConta
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public Guid ContaId { get; set; }
    public ContaBancaria? Conta { get; set; }

    /// <summary>O lançamento de onde o movimento veio. Nulo no que não vem de lançamento.</summary>
    public Guid? LancamentoId { get; set; }

    /// <summary>
    /// A transferência de que este movimento é uma ponta. As duas pontas levam o
    /// mesmo identificador, e se apagam juntas: uma sozinha seria dinheiro saindo
    /// de uma conta para lugar nenhum.
    /// </summary>
    public Guid? TransferenciaId { get; set; }

    /// <summary>O dia em que o dinheiro entrou ou saiu, que é o dia que o extrato mostra.</summary>
    public DateOnly Data { get; set; }

    public decimal Valor { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public string Origem { get; set; } = string.Empty;
    public DateTimeOffset CriadoEm { get; set; }

    /// <summary>O movimento de uma baixa: entra quando é a receber, sai quando é a pagar.</summary>
    public static MovimentoDeConta DaBaixa(Lancamento lancamento, Guid contaId, decimal valor, DateOnly data) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = lancamento.TenantId,
        ContaId = contaId,
        LancamentoId = lancamento.Id,
        Data = data,
        Valor = lancamento.Natureza == NaturezaLancamento.Pagar ? -valor : valor,
        Descricao = lancamento.Descricao,
        Origem = OrigensDeMovimento.Baixa,
        CriadoEm = DateTimeOffset.UtcNow,
    };
}
