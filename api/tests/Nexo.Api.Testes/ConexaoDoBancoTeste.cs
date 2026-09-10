using Microsoft.Extensions.Configuration;
using Nexo.Api.Dados;
using Npgsql;

namespace Nexo.Api.Testes;

/// <summary>
/// A tradução da URI que os PaaS entregam para a string que o Npgsql entende.
///
/// Vale testar porque o erro aqui é do tipo que não aparece na subida: a
/// aplicação sobe, o <c>/saude</c> responde, e só a primeira consulta quebra —
/// já em produção, com alguém olhando.
/// </summary>
public class ConexaoDoBancoTeste
{
    private static IConfiguration Configuracao(params (string Chave, string Valor)[] valores) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(valores.ToDictionary(v => v.Chave, v => (string?)v.Valor))
            .Build();

    [Fact]
    public void Traduz_a_uri_do_postgres_para_a_forma_do_npgsql()
    {
        var traduzida = ConexaoDoBanco.Normalizar(
            "postgresql://usuario:segredo@servidor.interno:5433/meubanco");

        var lida = new NpgsqlConnectionStringBuilder(traduzida);

        Assert.Equal("servidor.interno", lida.Host);
        Assert.Equal(5433, lida.Port);
        Assert.Equal("meubanco", lida.Database);
        Assert.Equal("usuario", lida.Username);
        Assert.Equal("segredo", lida.Password);
    }

    [Fact]
    public void Sem_porta_na_uri_assume_a_padrao_do_postgres()
    {
        var lida = new NpgsqlConnectionStringBuilder(
            ConexaoDoBanco.Normalizar("postgres://u:s@servidor/banco"));

        Assert.Equal(5432, lida.Port);
    }

    [Fact]
    public void Decodifica_senha_com_caracteres_que_a_uri_escapa()
    {
        /*
         * Senha gerada por PaaS costuma trazer @, / e +. Sem decodificar, a
         * senha chega truncada ou trocada — e o erro que volta é "autenticação
         * falhou", que manda procurar no lugar errado.
         */
        var lida = new NpgsqlConnectionStringBuilder(
            ConexaoDoBanco.Normalizar("postgresql://u%40dm:p%2Fss%2Bw%40rd@servidor:5432/banco"));

        Assert.Equal("u@dm", lida.Username);
        Assert.Equal("p/ss+w@rd", lida.Password);
    }

    [Fact]
    public void Leva_o_sslmode_da_uri_junto()
    {
        var lida = new NpgsqlConnectionStringBuilder(
            ConexaoDoBanco.Normalizar("postgresql://u:s@servidor:5432/banco?sslmode=require"));

        Assert.Equal(SslMode.Require, lida.SslMode);
    }

    [Fact]
    public void Quem_ja_vem_na_forma_do_npgsql_passa_intacto()
    {
        const string original = "Host=localhost;Port=5432;Database=nexo;Username=nexo;Password=nexo";

        Assert.Equal(original, ConexaoDoBanco.Normalizar(original));
    }

    [Fact]
    public void A_configuracao_explicita_vence_o_DATABASE_URL()
    {
        var resolvida = ConexaoDoBanco.Resolver(Configuracao(
            ("ConnectionStrings:Nexo", "Host=escolhido;Database=nexo"),
            ("DATABASE_URL", "postgresql://u:s@ignorado:5432/banco")));

        Assert.Contains("escolhido", resolvida);
    }

    [Fact]
    public void Sem_configuracao_explicita_usa_o_DATABASE_URL()
    {
        var lida = new NpgsqlConnectionStringBuilder(ConexaoDoBanco.Resolver(
            Configuracao(("DATABASE_URL", "postgresql://u:s@injetado:5432/banco"))));

        Assert.Equal("injetado", lida.Host);
    }

    [Fact]
    public void Sem_nenhuma_das_duas_a_mensagem_diz_o_que_configurar()
    {
        var erro = Assert.Throws<InvalidOperationException>(
            () => ConexaoDoBanco.Resolver(Configuracao()));

        // A mensagem precisa ensinar, não só reclamar.
        Assert.Contains("ConnectionStrings__Nexo", erro.Message);
        Assert.Contains("DATABASE_URL", erro.Message);
        Assert.Contains("postgresql://", erro.Message);
    }
}
