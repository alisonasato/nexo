namespace Nexo.Api.Dominio;

/// <summary>
/// A divisão de um valor em parcelas mensais.
///
/// <para>
/// <b>As parcelas somam exatamente o total.</b> A conta é feita em centavos
/// inteiros, e os centavos que sobram da divisão vão para a primeira parcela:
/// cem reais em três são 33,34, 33,33 e 33,33. Dividir em decimal e arredondar
/// cada parcela produziria três vezes 33,33, e um centavo sumiria do
/// escritório sem ninguém ver.
/// </para>
/// <para>
/// <b>Cada vencimento parte da primeira data, e não da parcela anterior.</b>
/// Começando em 31 de janeiro, as seguintes vencem em 28 de fevereiro e 31 de
/// março. Somar um mês à parcela anterior grudaria o dia 28 em todas depois de
/// fevereiro, e o cliente pagaria cada vez mais cedo.
/// </para>
/// </summary>
public static class Parcelas
{
    /// <summary>Cinco anos de parcelas mensais. Mais que isso é outro tipo de contrato.</summary>
    public const int Maximo = 60;

    public readonly record struct Parcela(int Numero, decimal Valor, DateOnly Vencimento);

    public static IReadOnlyList<Parcela> Dividir(decimal total, int quantidade, DateOnly primeiroVencimento)
    {
        if (quantidade is < 1 or > Maximo)
            throw new ArgumentOutOfRangeException(nameof(quantidade), quantidade, $"De 1 a {Maximo} parcelas.");

        var centavos = (long)decimal.Round(total * 100m, 0, MidpointRounding.AwayFromZero);

        if (centavos < quantidade)
            throw new ArgumentException("O total não chega a um centavo por parcela.", nameof(total));

        var porParcela = centavos / quantidade;
        var sobra = centavos % quantidade;

        var parcelas = new List<Parcela>(quantidade);

        for (var indice = 0; indice < quantidade; indice++)
        {
            var emCentavos = porParcela + (indice == 0 ? sobra : 0);

            /* AddMonths sobre a primeira data, com o índice: é o que devolve o
               dia 31 em março depois de um fevereiro de 28. */
            parcelas.Add(new Parcela(indice + 1, emCentavos / 100m, primeiroVencimento.AddMonths(indice)));
        }

        return parcelas;
    }
}
