namespace Nexo.Api.Dominio;

public enum SituacaoLancamento
{
    Aberto = 1,
    Pago = 2,
    Cancelado = 3,

    /// <summary>
    /// Substituído por parcelas novas numa renegociação.
    ///
    /// <para>
    /// <b>É situação própria, e não um cancelamento com outro nome.</b> O índice
    /// único de mensalidade só libera cancelados. Renegociado continua ocupando a
    /// competência, e é isso que impede gerar mensalidades de cobrar de novo um
    /// mês que já virou acordo. Se renegociar cancelasse o título, o próximo
    /// clique em "gerar" emitiria a mesma mensalidade uma segunda vez.
    /// </para>
    /// </summary>
    Renegociado = 4,
}

/// <summary>
/// Para que lado o dinheiro anda.
///
/// <para>
/// A natureza separa as duas metades do caixa, e por isso entra em toda soma:
/// um total que misturasse o que entra com o que sai não responderia pergunta
/// nenhuma.
/// </para>
/// </summary>
public enum NaturezaLancamento
{
    /// <summary>O escritório recebe: mensalidade, serviço avulso.</summary>
    Receber = 1,

    /// <summary>O escritório paga: aluguel, fornecedor, licença.</summary>
    Pagar = 2,
}

/// <summary>
/// Um valor a receber ou a pagar: a mensalidade de um contrato numa
/// competência, um serviço avulso, ou uma conta do próprio escritório.
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
/// porque a competência ficaria ocupada por um lançamento errado para sempre.
/// Cancelado sai da conta e libera a competência, sem sumir do histórico.
/// </para>
/// </summary>
public class Lancamento
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>
    /// A receber ou a pagar.
    ///
    /// <para>
    /// <b>Obrigatória, e sem padrão.</b> Um lançamento a pagar gravado como a
    /// receber por esquecimento inverteria o sentido do dinheiro e somaria no
    /// total errado, sem erro nenhum. O <c>required</c> obriga todo lugar que
    /// cria lançamento a dizer qual é, e o compilador aponta quem esqueceu.
    /// </para>
    /// </summary>
    public required NaturezaLancamento Natureza { get; set; }

    /// <summary>
    /// A pessoa do outro lado: cliente quando é a receber, fornecedor quando é
    /// a pagar.
    /// </summary>
    public Guid PessoaId { get; set; }
    public Pessoa? Pessoa { get; set; }

    /// <summary>Nulo quando o valor é avulso, sem contrato por trás.</summary>
    public Guid? ContratoId { get; set; }
    public Contrato? Contrato { get; set; }

    /// <summary>
    /// A recorrência que gerou este lançamento, quando foi uma. Nunca junto com
    /// contrato: o que vem de contrato é mensalidade de cliente.
    /// </summary>
    public Guid? RecorrenciaId { get; set; }

    public int CompetenciaAno { get; set; }
    public int CompetenciaMes { get; set; }

    public string Descricao { get; set; } = string.Empty;

    /// <summary>O valor original. Nunca é reescrito: o que muda na baixa fica na baixa.</summary>
    public decimal Valor { get; set; }
    public DateOnly Vencimento { get; set; }

    public SituacaoLancamento Situacao { get; set; } = SituacaoLancamento.Aberto;

    /* ----------------------------------------------------- parcelamento */

    /// <summary>
    /// O grupo das parcelas lançadas juntas. Nulo para quem foi lançado inteiro.
    ///
    /// Número e total ficam em campos, e não escritos na descrição, para a tela
    /// poder mostrar "2 de 3" do jeito que quiser e para a descrição continuar
    /// sendo o que foi cobrado.
    /// </summary>
    public Guid? ParcelamentoId { get; set; }
    public int? ParcelaNumero { get; set; }
    public int? ParcelasTotal { get; set; }

    /// <summary>
    /// O título que esta parcela substitui, quando ela nasceu de renegociação.
    ///
    /// É por aqui que o acordo se reconstrói: o título original fica como
    /// renegociado, e as parcelas novas apontam para ele.
    /// </summary>
    public Guid? RenegociadoDeId { get; set; }

    /* ---------------------------------------------------- classificação */

    /// <summary>O que o dinheiro foi, no plano de contas. Opcional: lançamento sem categoria continua valendo.</summary>
    public Guid? CategoriaId { get; set; }
    public Categoria? Categoria { get; set; }

    /// <summary>De quem o dinheiro é dentro do escritório. Opcional.</summary>
    public Guid? CentroDeCustoId { get; set; }
    public CentroDeCusto? CentroDeCusto { get; set; }

    /* --------------------------------------------------------- cobrança */

    /// <summary>
    /// O identificador da cobrança no PSP. Vazio enquanto ninguém cobrou.
    ///
    /// Ter valor aqui quer dizer que existe boleto e Pix emitidos lá fora, em
    /// nome deste lançamento. É por isso que cobrar duas vezes o mesmo lançamento
    /// é recusado: a segunda emissão não substituiria a primeira, criaria uma
    /// segunda cobrança do mesmo valor, e o cliente receberia dois boletos.
    /// </summary>
    public string CobrancaId { get; set; } = string.Empty;

    /// <summary>
    /// A página onde o cliente escolhe entre boleto e Pix e paga.
    ///
    /// É o endereço que o escritório manda para ele. Guardar o link direto do
    /// boleto no lugar deste fecharia a escolha na hora de mandar, que é antes
    /// de o cliente ter opinião.
    /// </summary>
    public string CobrancaUrl { get; set; } = string.Empty;

    /* ------------------------------------------------------------ baixa */

    /// <summary>
    /// Quanto entrou de fato. Pode diferir do valor: desconto combinado,
    /// pagamento parcial, juros de atraso.
    /// </summary>
    public decimal? ValorPago { get; set; }

    /// <summary>
    /// O que a baixa abateu do valor, em reais: desconto de pontualidade,
    /// abatimento combinado. Nulo quando não houve.
    ///
    /// <para>
    /// <b>Desconto, juros e multa explicam a diferença.</b> São valores
    /// digitados, e não taxas. Quando algum é informado, o valor pago precisa
    /// fechar com valor mais juros mais multa menos desconto; sem nenhum, o valor
    /// pago continua livre, como na baixa em lote e no aviso do PSP.
    /// </para>
    /// </summary>
    public decimal? Desconto { get; set; }

    /// <summary>Os juros de atraso cobrados na baixa, em reais. Nulo quando não houve.</summary>
    public decimal? Juros { get; set; }

    /// <summary>A multa de atraso cobrada na baixa, em reais. Nula quando não houve.</summary>
    public decimal? Multa { get; set; }

    /// <summary>Data em que o dinheiro entrou, não a data em que foi registrado.</summary>
    public DateOnly? PagoEm { get; set; }

    /// <summary>
    /// Como a baixa aconteceu: à mão, em lote ou por retorno do meio de
    /// cobrança. Guardado porque, quando o valor não bate, a primeira pergunta
    /// é sempre "quem deu essa baixa".
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
        Situacao == SituacaoLancamento.Aberto && Vencimento < hoje;
}

public static class OrigensDeBaixa
{
    public const string Manual = "manual";

    /// <summary>
    /// Baixado junto com outros, numa seleção. Separado de manual porque, quando
    /// o valor não bate, "foi no lote" já diz que ninguém digitou o valor: ele
    /// veio igual ao cobrado.
    /// </summary>
    public const string Lote = "lote";

    public const string Cobranca = "cobranca";
}
