namespace Nexo.Api.Dominio;

/// <summary>
/// Uma linha do plano de contas: o que o dinheiro foi.
///
/// <para>
/// <b>Em árvore, de até três níveis.</b> "Pessoal › Encargos › FGTS" é o fundo
/// que um escritório pequeno usa. Mais fundo que isso, classificar vira um
/// trabalho que ninguém faz no dia a dia, e o dinheiro passa a cair na primeira
/// categoria que aparece na lista.
/// </para>
/// <para>
/// <b>A natureza vem do topo.</b> Uma categoria a pagar debaixo de uma a receber
/// faria o total de despesas somar receita. A natureza se escolhe na raiz e é
/// herdada por toda a descendência, e não muda depois de criada.
/// </para>
/// <para>
/// <b>Não se apaga, inativa.</b> O que foi classificado nela continua precisando
/// dela para dizer o que foi.
/// </para>
/// </summary>
public class Categoria
{
    /// <summary>Grupo, categoria e subcategoria.</summary>
    public const int NiveisMaximos = 3;

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public string Nome { get; set; } = string.Empty;
    public NaturezaLancamento Natureza { get; set; }

    /// <summary>A categoria de cima. Nula na raiz.</summary>
    public Guid? PaiId { get; set; }

    public bool Ativa { get; set; } = true;

    public DateTimeOffset CriadoEm { get; set; }
    public DateTimeOffset AtualizadoEm { get; set; }
}
