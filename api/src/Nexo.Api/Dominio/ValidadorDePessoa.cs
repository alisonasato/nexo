using System.Text.RegularExpressions;

namespace Nexo.Api.Dominio;

/// <summary>
/// As regras do cadastro de pessoa.
///
/// Vieram do protótipo, onde já tinham sido escritas e depois corrigidas pelo
/// uso — em especial a decisão de <b>não</b> exigir endereço. Aqui elas passam
/// a valer de verdade: o front pode revalidar por experiência, mas quem diz o
/// que entra no banco é este arquivo.
///
/// A regra geral: recusar o que é <b>malformado</b>, nunca o que é
/// <b>incompleto</b>. Documento com dígito errado é erro de digitação; bairro
/// em branco é a vida real.
/// </summary>
public static partial class ValidadorDePessoa
{
    public static readonly string[] Ufs =
    [
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS",
        "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC",
        "SP", "SE", "TO",
    ];

    /// <param name="pessoa">O cadastro a conferir.</param>
    /// <param name="outraComMesmoDocumento">
    /// Cadastro já existente com o mesmo documento, se houver. Quem procura é
    /// quem tem o banco na mão; aqui só se escreve a mensagem, para que ela não
    /// nasça diferente em cada lugar que faz a busca.
    /// </param>
    public static List<Problema> Validar(Pessoa pessoa, Pessoa? outraComMesmoDocumento = null)
    {
        var problemas = new List<Problema>();
        var ehFisica = pessoa.Tipo == TipoPessoa.Fisica;
        var documento = Documento.ApenasDigitos(pessoa.Documento);

        /* --------------------------------------------------- identificação */

        if (string.IsNullOrWhiteSpace(pessoa.Nome))
        {
            problemas.Add(new Problema(
                "nome",
                TitulosDeProblema.Cadastro,
                ehFisica ? "O nome completo não foi informado." : "A razão social não foi informada.",
                ehFisica
                    ? "Digite o nome como consta no CPF, incluindo o sobrenome."
                    : "Digite a razão social exatamente como consta no cartão CNPJ."));
        }
        else if (ehFisica && pessoa.Nome.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2)
        {
            problemas.Add(new Problema(
                "nome",
                TitulosDeProblema.Cadastro,
                "O nome informado não inclui o sobrenome.",
                "Pessoa física exige o nome completo, como consta no CPF."));
        }

        if (documento.Length == 0)
        {
            problemas.Add(new Problema(
                "documento",
                TitulosDeProblema.Documento,
                ehFisica ? "O CPF não foi informado." : "O CNPJ não foi informado.",
                ehFisica
                    ? "Digite os 11 dígitos do CPF, sem pontos ou traço."
                    : "Digite os 14 dígitos do CNPJ, sem pontos, barra ou traço."));
        }
        else if (ehFisica && documento.Length != 11)
        {
            problemas.Add(new Problema(
                "documento",
                TitulosDeProblema.Documento,
                $"O CPF informado tem {documento.Length} dígitos, e não 11.",
                "Confira se não faltou nenhum dígito. Para empresa, troque o tipo para Jurídica."));
        }
        else if (!ehFisica && documento.Length != 14)
        {
            problemas.Add(new Problema(
                "documento",
                TitulosDeProblema.Documento,
                $"O CNPJ informado tem {documento.Length} dígitos, e não 14.",
                "Confira se não faltou nenhum dígito. Para pessoa física, troque o tipo para Física."));
        }
        else if (ehFisica && !Dominio.Documento.CpfValido(documento))
        {
            problemas.Add(new Problema(
                "documento",
                TitulosDeProblema.Documento,
                "O número do CPF informado é inválido.",
                "Verifique os dígitos verificadores e digite apenas números, sem pontos ou traço."));
        }
        else if (!ehFisica && !Dominio.Documento.CnpjValido(documento))
        {
            problemas.Add(new Problema(
                "documento",
                TitulosDeProblema.Documento,
                "O número do CNPJ informado é inválido.",
                "Verifique os dígitos verificadores e digite apenas números, sem pontos, barra ou traço."));
        }

        /*
         * Duplicidade é do documento, não do papel: a mesma pessoa pode ser
         * cliente e fornecedora, mas dois cadastros com o mesmo CNPJ são o
         * mesmo cadastro digitado duas vezes.
         */
        if (documento.Length > 0 && outraComMesmoDocumento is { } outra)
        {
            var nomeDaOutra = string.IsNullOrWhiteSpace(outra.NomeFantasia) ? outra.Nome : outra.NomeFantasia;
            problemas.Add(new Problema(
                "documento",
                TitulosDeProblema.Duplicidade,
                $"Este {(ehFisica ? "CPF" : "CNPJ")} já pertence a “{nomeDaOutra}”.",
                "Abra o cadastro existente em vez de criar um segundo registro."));
        }

        if (!ehFisica && !string.IsNullOrWhiteSpace(pessoa.InscricaoEstadual))
        {
            var inscricao = pessoa.InscricaoEstadual.Trim().ToUpperInvariant();
            var somenteDigitos = Dominio.Documento.ApenasDigitos(inscricao);

            if (inscricao != "ISENTO" && somenteDigitos.Length < 8)
            {
                problemas.Add(new Problema(
                    "inscricaoEstadual",
                    TitulosDeProblema.Cadastro,
                    "A inscrição estadual informada não tem um formato válido.",
                    "Informe apenas os números da inscrição (mínimo 8 dígitos) ou escreva ISENTO quando a empresa não for contribuinte de ICMS."));
            }
        }

        /* -------------------------------------------------------- contatos */

        if (!string.IsNullOrWhiteSpace(pessoa.Email) && !EmailValido(pessoa.Email.Trim()))
        {
            problemas.Add(new Problema(
                "email",
                TitulosDeProblema.Contato,
                "O e-mail informado não tem um formato válido.",
                "Use o formato nome@dominio.com.br, sem espaços."));
        }

        foreach (var (campo, rotulo, valor) in new[]
                 {
                     ("telefone", "telefone", pessoa.Telefone),
                     ("celular", "celular", pessoa.Celular),
                 })
        {
            var numero = Dominio.Documento.ApenasDigitos(valor);
            if (numero.Length > 0 && (numero.Length < 10 || numero.Length > 11))
            {
                problemas.Add(new Problema(
                    campo,
                    TitulosDeProblema.Contato,
                    $"O {rotulo} informado tem {numero.Length} dígitos.",
                    "Informe DDD e número: 10 dígitos para fixo e 11 para celular."));
            }
        }

        /* -------------------------------------------------------- endereço */

        var cep = Dominio.Documento.ApenasDigitos(pessoa.Endereco.Cep);
        if (cep.Length > 0 && cep.Length != 8)
        {
            problemas.Add(new Problema(
                "endereco.cep",
                TitulosDeProblema.Endereco,
                $"O CEP informado tem {cep.Length} dígitos, e não 8.",
                "Digite apenas os números do CEP, no formato 01310100."));
        }

        /*
         * Nada de endereço é obrigatório, nem quando o CEP está preenchido.
         * Só se recusa valor malformado naquilo que a pessoa de fato digitou.
         */
        var uf = pessoa.Endereco.Uf.Trim();
        if (uf.Length > 0 && !Ufs.Contains(uf.ToUpperInvariant()))
        {
            problemas.Add(new Problema(
                "endereco.uf",
                TitulosDeProblema.Endereco,
                $"A UF “{uf}” não é uma sigla de estado válida.",
                "Escolha o estado pela lista para garantir a sigla correta."));
        }

        return problemas;
    }

    private static bool EmailValido(string email) => ExpressaoDeEmail().IsMatch(email);

    /* Deliberadamente frouxa: recusa o obviamente errado, não tenta ser a RFC. */
    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]{2,}$")]
    private static partial Regex ExpressaoDeEmail();
}
