namespace Nexo.Api.Dominio;

/// <summary>
/// O regime tributário na Receita.
///
/// Mora em <see cref="Pessoa"/>, e não num papel: é propriedade da empresa,
/// verdadeira seja ela cliente do escritório, fornecedora ou nenhum dos dois.
/// </summary>
public enum RegimeTributario
{
    Mei = 1,
    SimplesNacional = 2,
    LucroPresumido = 3,
    LucroReal = 4,
    TerceiroSetor = 5,
    PessoaFisica = 6,
}
