namespace Nexo.Api.Dominio;

/// <summary>
/// CPF e CNPJ: dígitos verificadores, não formato.
///
/// A conferência aqui não substitui a Receita — ela só recusa o número que não
/// pode existir. Serve para pegar dígito trocado na digitação, que é o erro
/// comum, e não fraude.
/// </summary>
public static class Documento
{
    public static string ApenasDigitos(string? texto) =>
        string.IsNullOrEmpty(texto) ? string.Empty : new string(texto.Where(char.IsAsciiDigit).ToArray());

    public static bool CpfValido(string documento)
    {
        var digitos = ApenasDigitos(documento);
        if (digitos.Length != 11) return false;

        /*
         * Sequências repetidas passam no cálculo dos dígitos verificadores —
         * 111.111.111-11 fecha a conta — e nenhuma delas é CPF de ninguém.
         */
        if (digitos.All(digito => digito == digitos[0])) return false;

        return DigitoVerificador(digitos, 9, 10) == digitos[9] - '0'
            && DigitoVerificador(digitos, 10, 11) == digitos[10] - '0';

        static int DigitoVerificador(string numero, int tamanho, int pesoInicial)
        {
            var soma = 0;
            for (var i = 0; i < tamanho; i++)
                soma += (numero[i] - '0') * (pesoInicial - i);

            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }
    }

    public static bool CnpjValido(string documento)
    {
        var digitos = ApenasDigitos(documento);
        if (digitos.Length != 14) return false;
        if (digitos.All(digito => digito == digitos[0])) return false;

        int[] primeiroPeso = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] segundoPeso = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

        return DigitoVerificador(digitos, primeiroPeso) == digitos[12] - '0'
            && DigitoVerificador(digitos, segundoPeso) == digitos[13] - '0';

        static int DigitoVerificador(string numero, int[] pesos)
        {
            var soma = 0;
            for (var i = 0; i < pesos.Length; i++)
                soma += (numero[i] - '0') * pesos[i];

            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }
    }
}
