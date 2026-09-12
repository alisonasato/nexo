namespace Nexo.Api.Dominio;

/// <summary>
/// Um problema de validação, dito para quem vai corrigir.
///
/// As três partes existem porque uma mensagem só não resolve: <see cref="Titulo"/>
/// agrupa, <see cref="Descricao"/> diz o que está errado e <see cref="Sugestao"/>
/// diz o que fazer. "Documento inválido" sozinho manda a pessoa adivinhar; "o
/// CPF tem 10 dígitos, e não 11" ela conserta em cinco segundos.
///
/// O <see cref="Campo"/> é o caminho do campo no formulário — <c>endereco.cep</c>,
/// e não <c>Cep</c> — para o front conseguir apontar o erro no lugar certo.
/// </summary>
/// <param name="Campo">Caminho do campo, em minúsculas e separado por ponto.</param>
/// <param name="Titulo">Agrupador curto, repetido entre problemas do mesmo tipo.</param>
/// <param name="Descricao">O que está errado, com o dado concreto quando ajudar.</param>
/// <param name="Sugestao">O que fazer para corrigir.</param>
public record Problema(string Campo, string Titulo, string Descricao, string Sugestao);

public static class TitulosDeProblema
{
    public const string Cadastro = "Falha na validação do cadastro";
    public const string Documento = "Documento inválido";
    public const string Duplicidade = "Documento já cadastrado";
    public const string Endereco = "Endereço incompleto";
    public const string Contato = "Revise os dados de contato";
    public const string Vinculo = "Cadastro ainda em uso";
}
