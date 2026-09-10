using Microsoft.AspNetCore.Identity;

namespace Nexo.Api.Autenticacao;

/// <summary>
/// As mensagens do Identity em português.
///
/// O padrão é em inglês — "Passwords must have at least one digit" — e essas
/// frases chegam direto na tela de quem está tentando trocar a senha. Num
/// sistema todo em português, é a diferença entre uma instrução e um ruído.
///
/// Só o que a aplicação pode disparar hoje foi traduzido. O resto continua
/// vindo da base, em inglês: traduzir mensagem que ninguém vê é trabalho que
/// envelhece sozinho.
/// </summary>
public sealed class DescritorDeErros : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) => new()
    {
        Code = nameof(PasswordTooShort),
        Description = $"A senha precisa ter pelo menos {length} caracteres.",
    };

    public override IdentityError PasswordRequiresDigit() => new()
    {
        Code = nameof(PasswordRequiresDigit),
        Description = "A senha precisa ter pelo menos um número.",
    };

    public override IdentityError PasswordRequiresLower() => new()
    {
        Code = nameof(PasswordRequiresLower),
        Description = "A senha precisa ter pelo menos uma letra minúscula.",
    };

    public override IdentityError PasswordRequiresUpper() => new()
    {
        Code = nameof(PasswordRequiresUpper),
        Description = "A senha precisa ter pelo menos uma letra maiúscula.",
    };

    public override IdentityError PasswordRequiresNonAlphanumeric() => new()
    {
        Code = nameof(PasswordRequiresNonAlphanumeric),
        Description = "A senha precisa ter pelo menos um símbolo, como @, # ou !.",
    };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => new()
    {
        Code = nameof(PasswordRequiresUniqueChars),
        Description = $"A senha precisa ter pelo menos {uniqueChars} caracteres diferentes entre si.",
    };

    public override IdentityError PasswordMismatch() => new()
    {
        Code = nameof(PasswordMismatch),
        Description = "A senha atual está incorreta.",
    };

    public override IdentityError DuplicateEmail(string email) => new()
    {
        Code = nameof(DuplicateEmail),
        Description = $"O e-mail {email} já está em uso.",
    };

    public override IdentityError DuplicateUserName(string userName) => new()
    {
        Code = nameof(DuplicateUserName),
        Description = $"O usuário {userName} já existe.",
    };

    public override IdentityError InvalidEmail(string? email) => new()
    {
        Code = nameof(InvalidEmail),
        Description = $"O e-mail {email} não tem um formato válido.",
    };
}
