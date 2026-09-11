namespace Nexo.Api.Dominio;

/// <summary>
/// Códigos sequenciais visíveis.
///
/// <para>
/// Cliente é número puro — <c>1</c>, <c>2</c>, <c>13</c> — e contrato leva
/// prefixo e preenchimento: <c>C0001</c>. A diferença é escolha de quem usa, e
/// não acidente: o código do cliente é o que se fala ao telefone.
/// </para>
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
    /// <param name="digitos">
    /// Zeros à esquerda até este tamanho. <b>Zero quer dizer sem preenchimento</b>,
    /// que é o caso do cliente.
    /// </param>
    public static string Proximo(string prefixo, IEnumerable<string> existentes, int digitos = 4)
    {
        var maior = existentes
            .Where(codigo => codigo.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))
            .Select(codigo =>
                int.TryParse(codigo.AsSpan(prefixo.Length), out var numero) ? numero : 0)
            .DefaultIfEmpty(0)
            .Max();

        var numeroNovo = (maior + 1).ToString();

        /*
         * O preenchimento é montado à mão, e não por formato de número, porque
         * formato vazio tem comportamento próprio no .NET e este é o tipo de
         * detalhe que se descobre em produção.
         */
        return prefixo + numeroNovo.PadLeft(digitos, '0');
    }
}
