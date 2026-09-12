namespace Nexo.Api.Autenticacao;

/// <summary>
/// Como o token de sessão é assinado e por quanto tempo vale.
///
/// A chave vem da configuração e <b>não tem valor padrão</b>: em produção, uma
/// chave omitida derruba a aplicação na subida em vez de assinar tokens com um
/// segredo que está no repositório. Fora de desenvolvimento, use
/// <c>dotnet user-secrets</c> ou variável de ambiente.
/// </summary>
public sealed class OpcoesDeToken
{
    public const string Secao = "Jwt";

    /// <summary>Mínimo de 32 bytes: é o que HMAC-SHA256 exige.</summary>
    public string Chave { get; set; } = string.Empty;

    public string Emissor { get; set; } = "nexo";
    public string Publico { get; set; } = "nexo";

    /// <summary>Uma jornada de trabalho. Quem sai, entra de novo no dia seguinte.</summary>
    public int HorasDeValidade { get; set; } = 8;

    /// <summary>
    /// Quanto tempo uma sessão pode ser renovada, contado da entrada.
    ///
    /// <para>
    /// A renovação é deslizante: quem está usando o sistema não é interrompido.
    /// Sem um teto, porém, uma aba esquecida aberta sustentaria a mesma sessão
    /// para sempre, e um cookie roubado junto com ela. O teto é o que garante
    /// que toda sessão termina — a senha continua sendo o jeito de terminar
    /// antes, derrubando todas de uma vez.
    /// </para>
    /// </summary>
    public int DiasDeSessao { get; set; } = 7;
}
