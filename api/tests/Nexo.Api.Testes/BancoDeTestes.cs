using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dados;

namespace Nexo.Api.Testes;

/// <summary>
/// O banco onde os testes de integração rodam (decisão Q30).
///
/// É Postgres de verdade, e tem que ser: a RLS é um comportamento do banco, e
/// um banco em memória provaria exatamente nada. A conexão vem da variável de
/// ambiente <c>NEXO_TESTES_CONEXAO</c> para que o mesmo teste sirva a um
/// Postgres local, a um contêiner ou a um banco de desenvolvimento hospedado.
/// </summary>
public sealed class BancoDeTestes : IAsyncLifetime
{
    public const string PadraoDeConexao =
        "Host=localhost;Port=5432;Database=nexo_testes;Username=nexo;Password=nexo";

    public string Conexao { get; } =
        Environment.GetEnvironmentVariable("NEXO_TESTES_CONEXAO") ?? PadraoDeConexao;

    public async Task InitializeAsync()
    {
        await using var contexto = Criar(tenant: null);
        await contexto.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Um contexto falando como o tenant informado. Passar <c>null</c> é o caso
    /// interessante: significa “sem tenant”, e sem tenant não se enxerga nada.
    /// </summary>
    public NexoDbContext Criar(Guid? tenant)
    {
        var contextoDeTenant = new ContextoDeTenantFixo(tenant);

        var opcoes = new DbContextOptionsBuilder<NexoDbContext>()
            .UseNpgsql(Conexao)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new InterceptorDeTenant(contextoDeTenant))
            .Options;

        return new NexoDbContext(opcoes);
    }
}

[CollectionDefinition(nameof(ColecaoDoBanco))]
public sealed class ColecaoDoBanco : ICollectionFixture<BancoDeTestes>;
