namespace Nexo.Api.Dominio;

/// <summary>
/// Códigos sequenciais visíveis, do tipo C-0001.
/// </summary>
public static class Codigos
{
    /// <summary>
    /// O próximo código, seguindo o <b>maior já usado</b> — nunca a quantidade
    /// de registros.
    ///
    /// A diferença aparece na primeira exclusão: com dez cadastros, apagando um
    /// e criando outro, contar registros devolve o código de alguém que ainda
    /// existe. O banco recusa pelo índice único, e a pessoa vê um erro que não
    /// tem como resolver. Seguir o maior nunca colide, e deixar buracos na
    /// sequência é normal — código é identificador, não contagem.
    /// </summary>
    public static string Proximo(string prefixo, IEnumerable<string> existentes, int digitos = 4)
    {
        var maior = existentes
            .Where(codigo => codigo.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))
            .Select(codigo =>
                int.TryParse(codigo.AsSpan(prefixo.Length), out var numero) ? numero : 0)
            .DefaultIfEmpty(0)
            .Max();

        return prefixo + (maior + 1).ToString(new string('0', digitos));
    }
}
