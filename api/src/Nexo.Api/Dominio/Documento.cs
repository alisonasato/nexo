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

    /// <summary>
    /// O CNPJ como se guarda: maiúsculas, sem pontuação, letras preservadas.
    ///
    /// <para>
    /// <b>Não use <see cref="ApenasDigitos"/> em CNPJ.</b> Desde 31 de julho de
    /// 2026 a Receita emite CNPJ alfanumérico para inscrição nova (IN RFB
    /// 2.229/2024): as 12 primeiras posições aceitam <c>0-9</c> e <c>A-Z</c>, e
    /// só os 2 dígitos verificadores continuam numéricos. Limpar para dígitos
    /// transformaria o CNPJ de um cliente novo em outro documento, mais curto,
    /// que não é de ninguém — e o erro seria silencioso.
    /// </para>
    /// <para>
    /// Minúsculas viram maiúsculas em vez de serem recusadas: quem digita
    /// <c>12abc34501de35</c> escreveu o documento certo com a tecla errada.
    /// </para>
    /// </summary>
    public static string NormalizarCnpj(string? texto) =>
        string.IsNullOrEmpty(texto)
            ? string.Empty
            : new string(texto.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

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

    /// <summary>
    /// Vale para o CNPJ numérico de sempre e para o alfanumérico novo.
    ///
    /// <para>
    /// A conta é a mesma dos dois: módulo 11, com os mesmos pesos. O que muda é
    /// o valor de cada caractere, que passa a ser <b>o código ASCII menos
    /// 48</b> — e essa subtração é exatamente o que <c>caractere - '0'</c> já
    /// fazia. Para dígito dá o próprio dígito; para letra dá 17 no <c>A</c>, 18
    /// no <c>B</c>, e assim por diante. O código de cálculo não mudou uma linha:
    /// o que mudou foi parar de jogar as letras fora antes de chegar aqui.
    /// </para>
    /// <para>
    /// Conferido contra o exemplo oficial do Serpro, <c>12ABC34501DE35</c>, que
    /// está nos testes. Sem um número de fora, a implementação só provaria que
    /// concorda consigo mesma.
    /// </para>
    /// </summary>
    public static bool CnpjValido(string documento)
    {
        var digitos = NormalizarCnpj(documento);
        if (digitos.Length != 14) return false;
        if (digitos.All(digito => digito == digitos[0])) return false;

        /* A raiz aceita letra; os dois verificadores, não. */
        if (!digitos.Take(12).All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c))) return false;
        if (!char.IsAsciiDigit(digitos[12]) || !char.IsAsciiDigit(digitos[13])) return false;

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
