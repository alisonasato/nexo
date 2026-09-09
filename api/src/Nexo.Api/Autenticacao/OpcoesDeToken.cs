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
}
