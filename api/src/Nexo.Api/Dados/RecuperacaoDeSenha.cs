using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dominio;

namespace Nexo.Api.Dados;

/// <summary>
/// Redefine a senha de um usuário na subida, a partir de configuração.
///
/// <para>
/// <b>Existe porque não há recuperação de senha, e perder a senha trancava o
/// sistema para sempre.</b> O provisionamento não resolve: ele só age com o
/// banco vazio, então quem já tem dado não consegue mais entrar nem
/// reprovisionar. A saída era SQL na mão — e não é, porque o Identity guarda a
/// senha em hash com formato próprio, que não se escreve à mão.
/// </para>
/// <para>
/// A variável de ambiente é o lugar certo porque é o único que existe. Quem
/// implanta num PaaS tem o painel e mais nada; não há terminal, não há
/// <c>psql</c>, e pedir a string de conexão do banco de produção para rodar um
/// comando de fora é pior em todo sentido.
/// </para>
/// <para>
/// <b>Isto não cria poder novo.</b> Quem consegue definir variáveis de ambiente
/// já podia trocar a string de conexão, apontar para outro banco, ou ler tudo.
/// O que muda é que agora existe um caminho previsto, que grita no registro e
/// pede para ser apagado — em vez de um caminho improvisado, que ninguém
/// documenta e todo mundo repete.
/// </para>
/// </summary>
public static class RecuperacaoDeSenha
{
    public const string Secao = "Recuperacao";

    public record Pedido(string Email, string Senha);

    /// <summary>O que redefinir, ou <c>null</c> quando não há nada a fazer.</summary>
    public static Pedido? Ler(IConfiguration configuracao)
    {
        var secao = configuracao.GetSection(Secao);

        var email = secao["Email"];
        var senha = secao["Senha"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(senha)) return null;

        return new Pedido(email.Trim(), senha);
    }

    public static async Task ExecutarAsync(
        IServiceProvider servicos,
        Pedido pedido,
        CancellationToken cancelamento = default)
    {
        using var escopo = servicos.CreateScope();
        var provedor = escopo.ServiceProvider;

        var usuarios = provedor.GetRequiredService<UserManager<Usuario>>();
        var registro = provedor.GetRequiredService<ILoggerFactory>().CreateLogger("Recuperação");

        var usuario = await usuarios.Users
            .FirstOrDefaultAsync(u => u.NormalizedEmail == pedido.Email.ToUpperInvariant(), cancelamento);

        if (usuario is null)
        {
            /*
             * Não cria. Criar usuário sem tenant deixaria alguém dentro do
             * sistema sem escritório nenhum, e criar um tenant junto é
             * provisionamento — que tem regra própria e trava própria.
             */
            registro.LogWarning(
                "Nenhum usuário com o e-mail informado em {Secao}__Email. Nada foi alterado. " +
                "Confira o e-mail; ele precisa ser o mesmo com que a conta foi criada.", Secao);
            return;
        }

        /*
         * A nova senha é conferida ANTES de a antiga sair, e a ordem é a coisa
         * mais importante deste arquivo.
         *
         * Tirar primeiro e descobrir depois que a nova não passa nas regras
         * deixaria o usuário sem senha nenhuma — trancado de vez, pela
         * ferramenta que existe justamente para destrancar. Não é hipótese: foi
         * o que aconteceu na primeira versão, e o teste da senha fraca é o que
         * pegou.
         */
        foreach (var validador in usuarios.PasswordValidators)
        {
            var conferencia = await validador.ValidateAsync(usuarios, usuario, pedido.Senha);
            if (conferencia.Succeeded) continue;

            registro.LogWarning(
                "A senha informada não passa nas regras, e nada foi alterado — a senha atual " +
                "continua valendo. {Erros}",
                string.Join(" ", conferencia.Errors.Select(erro => erro.Description)));
            return;
        }

        /*
         * Tira e põe, em vez de gerar um token de redefinição.
         *
         * O token é assinado com as chaves do DataProtection, que neste
         * contêiner não sobrevivem a um reinício — está nos avisos da subida. Ele
         * até funcionaria, porque é gerado e usado no mesmo processo, mas fazer a
         * única saída de emergência depender da parte mais frágil da instalação
         * é pedir para ela falhar justamente no dia em que é necessária.
         *
         * Isto também roda o carimbo de segurança, então toda sessão aberta cai.
         * É o que se quer: quem perdeu a senha não sabe quem mais está dentro.
         */
        await usuarios.RemovePasswordAsync(usuario);
        var resultado = await usuarios.AddPasswordAsync(usuario, pedido.Senha);

        if (!resultado.Succeeded)
        {
            /* Quase sempre a senha é fraca demais para as regras do Identity. */
            registro.LogWarning(
                "Não foi possível redefinir a senha: {Erros}",
                string.Join(" ", resultado.Errors.Select(erro => erro.Description)));
            return;
        }

        /*
         * Alto e repetido. Enquanto as variáveis existirem, toda subida redefine
         * a senha de novo — inclusive por cima de uma que a pessoa tenha trocado
         * pela tela depois. O aviso é o que impede isso virar armadilha.
         */
        registro.LogWarning(
            "A senha de {Email} foi redefinida pela configuração. APAGUE AS DUAS VARIÁVEIS DE " +
            "{Variavel} (Email e Senha) AGORA: enquanto existirem, toda subida refaz esta troca — " +
            "inclusive por cima de uma senha trocada depois pela tela. As sessões abertas caíram.",
            pedido.Email, Secao);
    }
}
