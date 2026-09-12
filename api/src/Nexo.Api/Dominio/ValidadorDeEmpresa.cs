namespace Nexo.Api.Dominio;

/// <summary>
/// O que a empresa do escritório precisa ter para ser gravada.
///
/// <para>
/// Mais exigente que o cadastro de pessoas, e de propósito. Uma pessoa pode
/// entrar pela metade porque o cliente ligou e o resto chega depois; a empresa
/// é o próprio estabelecimento, e razão social e CNPJ dela vão sair na nota e
/// no boleto. Aqui o dado incompleto não é a vida real, é erro.
/// </para>
/// </summary>
public static class ValidadorDeEmpresa
{
    /// <param name="outraComMesmoCnpj">
    /// Empresa já existente com o mesmo CNPJ, se houver. Quem procura é quem
    /// tem o banco na mão; aqui só se escreve a mensagem, para que ela não
    /// nasça diferente em cada lugar que faz a busca.
    /// </param>
    public static List<Problema> Validar(Empresa empresa, Empresa? outraComMesmoCnpj = null)
    {
        var problemas = new List<Problema>();
        var cnpj = Documento.NormalizarCnpj(empresa.Cnpj);

        if (string.IsNullOrWhiteSpace(empresa.RazaoSocial))
        {
            problemas.Add(new Problema(
                "razaoSocial",
                TitulosDeProblema.Cadastro,
                "A razão social não foi informada.",
                "Digite a razão social exatamente como consta no cartão CNPJ."));
        }

        if (cnpj.Length == 0)
        {
            problemas.Add(new Problema(
                "cnpj",
                TitulosDeProblema.Documento,
                "O CNPJ não foi informado.",
                "Digite as 14 posições do CNPJ, sem pontos, barra ou traço."));
        }
        else if (cnpj.Length != 14)
        {
            problemas.Add(new Problema(
                "cnpj",
                TitulosDeProblema.Documento,
                $"O CNPJ informado tem {cnpj.Length} posições, e não 14.",
                "Confira se não faltou nada."));
        }
        else if (!Documento.CnpjValido(cnpj))
        {
            problemas.Add(new Problema(
                "cnpj",
                TitulosDeProblema.Documento,
                "O CNPJ informado não passa na conferência dos dígitos.",
                "Confira o número no cartão CNPJ. Ele pode ter letras nas 12 primeiras posições, mas os dois últimos são sempre números."));
        }
        else if (outraComMesmoCnpj is not null)
        {
            problemas.Add(new Problema(
                "cnpj",
                TitulosDeProblema.Duplicidade,
                $"Este CNPJ já é o de “{outraComMesmoCnpj.RazaoSocial}”.",
                "Matriz e filial têm CNPJs diferentes. Confira qual das duas você está editando."));
        }

        return problemas;
    }
}
