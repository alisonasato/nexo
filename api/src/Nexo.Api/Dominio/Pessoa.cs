namespace Nexo.Api.Dominio;

public enum TipoPessoa
{
    Fisica = 1,
    Juridica = 2,
}

/// <summary>
/// Endereço. Nenhum campo é obrigatório, nem quando o CEP está preenchido.
///
/// O cadastro incompleto é o caso comum, não a exceção: o cliente chega por
/// telefone, a consulta de CEP falha, a planilha importada só tinha o que
/// tinha. Barrar a gravação por isso obriga a inventar dado para o sistema
/// aceitar o verdadeiro — que é o pior desfecho possível.
/// </summary>
public class Endereco
{
    public string Cep { get; set; } = string.Empty;
    public string Logradouro { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string Complemento { get; set; } = string.Empty;
    public string Bairro { get; set; } = string.Empty;
    public string Cidade { get; set; } = string.Empty;
    public string Uf { get; set; } = string.Empty;
}

/// <summary>
/// Uma pessoa física ou jurídica com quem o escritório se relaciona.
///
/// É um cadastro só, e não um por papel: a mesma empresa pode ser cliente e
/// fornecedora, e dois cadastros com o mesmo CNPJ são o mesmo cadastro
/// digitado duas vezes. O que muda de um papel para outro é o vínculo, que
/// vive em outra tabela — quando existir.
/// </summary>
public class Pessoa
{
    public Guid Id { get; set; }

    /// <summary>Dono da linha. A política de RLS compara este campo.</summary>
    public Guid TenantId { get; set; }

    public TipoPessoa Tipo { get; set; }

    /// <summary>Nome completo, para física; razão social, para jurídica.</summary>
    public string Nome { get; set; } = string.Empty;

    public string NomeFantasia { get; set; } = string.Empty;

    /// <summary>CPF ou CNPJ, guardado só com dígitos.</summary>
    public string Documento { get; set; } = string.Empty;

    public string InscricaoEstadual { get; set; } = string.Empty;
    public string InscricaoMunicipal { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string Telefone { get; set; } = string.Empty;
    public string Celular { get; set; } = string.Empty;

    public Endereco Endereco { get; set; } = new();

    public string Observacoes { get; set; } = string.Empty;

    /// <summary>
    /// Inativo em vez de apagado. Quem já apareceu num contrato ou numa
    /// cobrança não pode sumir do histórico.
    /// </summary>
    public bool Ativo { get; set; } = true;

    public DateTimeOffset CriadoEm { get; set; }
    public DateTimeOffset AtualizadoEm { get; set; }
}
