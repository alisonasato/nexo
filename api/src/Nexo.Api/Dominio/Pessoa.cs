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
/// <para>
/// <b>É um cadastro só, e o papel é rótulo.</b> A mesma empresa pode ser
/// cliente e fornecedora ao mesmo tempo, e dois cadastros com o mesmo CNPJ são
/// o mesmo cadastro digitado duas vezes. Os papéis vivem em
/// <see cref="PessoaPapel"/>, e uma pessoa pode ter quantos couberem.
/// </para>
/// <para>
/// Houve uma tabela <c>clientes</c> aqui, apontando para esta. Ela carregava
/// código, regime tributário e responsável — e nenhum dos três era do vínculo:
/// o regime é o regime da empresa na Receita, cliente ou não; o responsável é
/// quem no escritório cuida dela; o código é o número dela. Campo que continua
/// verdadeiro depois de o papel acabar é da pessoa, e por isso desceram para cá.
/// </para>
/// </summary>
public class Pessoa
{
    public Guid Id { get; set; }

    /// <summary>Dono da linha. A política de RLS compara este campo.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Código sequencial visível, número puro: 1, 2, 13.</summary>
    public string Codigo { get; set; } = string.Empty;

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
    /// O regime na Receita. É da empresa, e vale seja ela cliente, fornecedora
    /// ou nenhum dos dois.
    /// </summary>
    public RegimeTributario RegimeTributario { get; set; } = RegimeTributario.SimplesNacional;

    /// <summary>Quem no escritório cuida desta pessoa.</summary>
    public string Responsavel { get; set; } = string.Empty;

    public ICollection<PessoaPapel> Papeis { get; set; } = [];

    /// <summary>
    /// Inativo em vez de apagado. Quem já apareceu num contrato ou numa
    /// cobrança não pode sumir do histórico.
    ///
    /// <para>
    /// Diferente de perder um papel: deixar de ser cliente é não ter mais o
    /// rótulo, e a pessoa segue ativa no cadastro. Inativar é sair do cadastro.
    /// </para>
    /// </summary>
    public bool Ativo { get; set; } = true;

    /// <summary>
    /// O identificador desta pessoa no PSP. Vazio até a primeira cobrança.
    ///
    /// <para>
    /// O PSP tem cadastro próprio de pagador, e a cobrança aponta para ele. Sem
    /// guardar o identificador aqui, cada cobrança criaria um pagador novo lá —
    /// o mesmo cliente repetido uma vez por mensalidade, e o histórico dele
    /// espalhado por dezenas de cadastros que ninguém consegue juntar.
    /// </para>
    /// <para>
    /// Só é preenchido para quem é cobrado. Fornecedor e colaborador nunca
    /// chegam lá, e não deveriam.
    /// </para>
    /// </summary>
    public string ClienteNoAsaas { get; set; } = string.Empty;

    public DateTimeOffset CriadoEm { get; set; }
    public DateTimeOffset AtualizadoEm { get; set; }
}
