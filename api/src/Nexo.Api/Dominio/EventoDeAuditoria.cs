using System.Text.Json;

namespace Nexo.Api.Dominio;

/// <summary>O que a trilha acompanha: o que mexe em dinheiro.</summary>
public enum EntidadeAuditada
{
    Lancamento = 1,
    MovimentoDeConta = 2,
    ContaBancaria = 3,
}

public enum AcaoDeAuditoria
{
    Criado = 1,
    Alterado = 2,
    Apagado = 3,
}

/// <summary>
/// Um campo que mudou, com o valor de antes e o de depois.
///
/// <para>
/// Os valores vão em texto neutro, igual em qualquer servidor: número com ponto
/// e duas casas, data ISO, enumeração pelo nome. Formatar é trabalho da tela.
/// </para>
/// </summary>
/// <param name="Antes">Nulo no que foi criado, e no campo que estava vazio.</param>
/// <param name="Depois">Nulo no que foi apagado, e no campo que ficou vazio.</param>
public record MudancaDeCampo(string Campo, string? Antes, string? Depois);

/// <summary>
/// Uma linha da trilha de auditoria: quem mexeu, quando, em quê, e o que mudou.
///
/// <para>
/// <b>Só se acrescenta.</b> Um gatilho no banco recusa alterar ou apagar evento,
/// venha de onde vier. Uma trilha que se deixa reescrever responde "quem deu
/// essa baixa" com o que alguém quis que ela respondesse.
/// </para>
/// <para>
/// <b>Não aponta para nada com chave estrangeira.</b> Ela registra também o que
/// deixou de existir, como o movimento que o estorno apagou, e precisa continuar
/// legível depois disso.
/// </para>
/// </summary>
public class EventoDeAuditoria
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public EntidadeAuditada Entidade { get; set; }
    public Guid EntidadeId { get; set; }

    /// <summary>
    /// O lançamento a que o evento diz respeito: o próprio, ou o da baixa que
    /// gravou o movimento. É por aqui que o histórico de um lançamento acha também
    /// o movimento que a baixa lançou e o estorno apagou.
    /// </summary>
    public Guid? LancamentoId { get; set; }

    /// <summary>A conta a que o evento diz respeito: a própria, ou a do movimento.</summary>
    public Guid? ContaId { get; set; }

    public AcaoDeAuditoria Acao { get; set; }

    /// <summary>A lista de <see cref="MudancaDeCampo"/>, em JSON.</summary>
    public string Mudancas { get; set; } = "[]";

    /// <summary>Nulo quando quem mexeu não foi uma pessoa com sessão, como o aviso do PSP.</summary>
    public Guid? UsuarioId { get; set; }

    /// <summary>
    /// Quem mexeu, como era no momento: o e-mail da sessão, "Asaas" no aviso do
    /// PSP, "Sistema" no que não veio de requisição nenhuma. O e-mail fica para
    /// quem deixar o escritório: o nome sai do cadastro, e ela não estará mais lá.
    /// </summary>
    public string Autor { get; set; } = string.Empty;

    public DateTimeOffset Em { get; set; }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Escrever(List<MudancaDeCampo> mudancas) =>
        JsonSerializer.Serialize(mudancas, Json);

    public List<MudancaDeCampo> LerMudancas() =>
        JsonSerializer.Deserialize<List<MudancaDeCampo>>(Mudancas, Json) ?? [];
}
