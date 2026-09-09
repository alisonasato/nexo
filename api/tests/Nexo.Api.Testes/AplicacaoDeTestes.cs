using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Nexo.Api.Testes;

/// <summary>
/// A API inteira subida em memória, para os testes que passam pela borda HTTP.
///
/// O ambiente é <c>Testes</c>, e não <c>Development</c>, por dois motivos: a
/// semeadura de demonstração não roda (cada teste monta os próprios dados) e
/// não há redirecionamento para https, que faria o cliente seguir um destino
/// inexistente.
/// </summary>
public sealed class AplicacaoDeTestes : WebApplicationFactory<Program>
{
    /*
     * A configuração vai por variável de ambiente, e não pelo
     * ConfigureAppConfiguration da fábrica, porque as duas coisas acontecem em
     * ordens diferentes: o Program.cs lê a chave de assinatura e derruba a
     * aplicação se ela faltar — de propósito — antes de `builder.Build()`, que
     * é justamente onde a fábrica injetaria a configuração dela. Variável de
     * ambiente é uma fonte que o CreateBuilder já lê, então chega a tempo.
     *
     * Afrouxar a verificação de arranque para agradar o teste seria trocar uma
     * defesa real por conveniência: sem chave, produção tem que cair.
     */
    public AplicacaoDeTestes(string conexao)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Nexo", conexao);
        Environment.SetEnvironmentVariable("Jwt__Chave", ChaveDeTestes);
        Environment.SetEnvironmentVariable("Jwt__Emissor", "nexo");
        Environment.SetEnvironmentVariable("Jwt__Publico", "nexo");
    }

    private const string ChaveDeTestes = "chave-de-testes-com-folga-de-tamanho-para-hmac-sha256";

    protected override void ConfigureWebHost(IWebHostBuilder construtor) =>
        construtor.UseEnvironment(Api.Ambientes.Testes);
}
