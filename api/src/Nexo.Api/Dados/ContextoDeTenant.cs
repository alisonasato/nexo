namespace Nexo.Api.Dados;

/// <summary>
/// Quem é o tenant desta requisição.
///
/// No passo 3 isto passa a ler a claim do JWT (decisão Q26). Enquanto a
/// autenticação não existe, a única implementação registrada devolve nulo — e
/// nulo significa <b>nenhuma linha</b>, não “todas”. Essa é a escolha
/// deliberada: a falha é fechada, não aberta.
/// </summary>
public interface IContextoDeTenant
{
    Guid? TenantAtual { get; }
}

/// <summary>
/// Implementação com valor fixo. Usada pelos testes e, por ora, pela API —
/// onde devolve nulo até a autenticação entrar.
/// </summary>
public sealed class ContextoDeTenantFixo(Guid? tenant = null) : IContextoDeTenant
{
    public Guid? TenantAtual { get; set; } = tenant;
}
