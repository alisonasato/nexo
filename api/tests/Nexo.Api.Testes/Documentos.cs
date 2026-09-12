namespace Nexo.Api.Testes;

/// <summary>
/// Documentos válidos para os testes montarem cadastros.
///
/// Estava copiado em três arquivos de teste, e o quarto uso foi o que cobrou a
/// extração — que é a mesma regra da decisão Q20: junta quando a repetição já
/// mostrou o que se repete, não antes.
/// </summary>
public static class Documentos
{
    /// <summary>
    /// Um CNPJ novo a cada chamada, com dígitos verificadores certos.
    ///
    /// Precisa ser novo porque o documento é único dentro do tenant, e precisa
    /// ser válido porque o cadastro confere os dígitos — um CNPJ inventado à
    /// mão faria o teste falhar na validação em vez de exercitar o que ele quer.
    /// </summary>
    public static string CnpjValido()
    {
        var base12 = Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString();

        int[] primeiroPeso = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] segundoPeso = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

        var primeiro = Digito(base12, primeiroPeso);
        var segundo = Digito(base12 + primeiro, segundoPeso);
        return base12 + primeiro + segundo;

        static int Digito(string numero, int[] pesos)
        {
            var soma = numero.Select((caractere, indice) => (caractere - '0') * pesos[indice]).Sum();
            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }
    }
}
