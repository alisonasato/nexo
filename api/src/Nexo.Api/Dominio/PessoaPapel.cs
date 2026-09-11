namespace Nexo.Api.Dominio;

/// <summary>
/// O que uma pessoa é para o escritório.
///
/// <para>
/// São rótulos, e uma pessoa pode ter vários ao mesmo tempo — o contador é
/// cliente de si mesmo, e a gráfica que imprime os carnês costuma ser
/// fornecedora e cliente.
/// </para>
/// <para>
/// Lista fechada, como todo o resto do sistema: regime tributário, situação de
/// contrato, tipo de pessoa. Uma tabela de papéis abriria a porta para existir
/// no banco um papel que o código não sabe tratar, e cobraria uma junção em
/// toda consulta para comprar flexibilidade que ninguém pediu.
/// </para>
/// </summary>
public enum Papel
{
    Cliente = 1,
    Fornecedor = 2,
    Vendedor = 3,
    Colaborador = 4,
}

/// <summary>
/// A ligação entre uma pessoa e um papel.
///
/// <para>
/// <b>A linha é o fato.</b> Ter o papel de cliente é ter esta linha; deixar de
/// ser cliente é não tê-la. Não existe "papel inativo": inativo seria um
/// terceiro estado entre ser e não ser, e ninguém saberia dizer o que ele
/// significa numa listagem.
/// </para>
/// <para>
/// Quem some é o rótulo, nunca a pessoa. O histórico não se perde porque
/// contrato e recebível apontam para a pessoa, e ela continua lá.
/// </para>
/// </summary>
public class PessoaPapel
{
    /// <summary>Dono da linha. A política de RLS compara este campo.</summary>
    public Guid TenantId { get; set; }

    public Guid PessoaId { get; set; }
    public Pessoa? Pessoa { get; set; }

    public Papel Papel { get; set; }

    public DateTimeOffset CriadoEm { get; set; }
}
