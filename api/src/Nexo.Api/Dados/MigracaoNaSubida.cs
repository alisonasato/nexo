using Microsoft.EntityFrameworkCore;

namespace Nexo.Api.Dados;

/// <summary>
/// Aplica as migrações pendentes quando a aplicação sobe.
///
/// <para>
/// Antes disso, só o ambiente de desenvolvimento migrava — de carona na
/// semeadura — e os testes. Em produção nada migrava: implantar daria um banco
/// vazio, e toda consulta falharia em cima de tabela que não existe.
/// </para>
/// <para>
/// <b>O trinco.</b> No PaaS (decisão Q31) pode haver mais de uma instância, e
/// numa implantação elas sobem juntas. Duas rodando <c>Migrate</c> ao mesmo
/// tempo tentam criar a mesma tabela, e uma das duas morre na subida. O
/// <c>pg_advisory_lock</c> resolve: a segunda espera a primeira terminar e,
/// quando entra, não encontra mais nada pendente.
/// </para>
/// <para>
/// A conexão é aberta à mão de propósito. Trinco consultivo pertence à
/// <b>sessão</b>: se o EF abrisse e fechasse a conexão entre o trinco e a
/// migração, o trinco se perderia no caminho e não protegeria nada.
/// </para>
/// </summary>
public static class MigracaoNaSubida
{
    /*
     * Um número qualquer, mas fixo e exclusivo deste uso. O espaço de trincos
     * consultivos do Postgres é global por banco: dois usos diferentes com o
     * mesmo número virariam um trinco só, e um esperaria pelo outro sem motivo.
     */
    private const long Trinco = 7_913_204_001;

    public static async Task AplicarAsync(IServiceProvider servicos, CancellationToken cancelamento = default)
    {
        using var escopo = servicos.CreateScope();
        var provedor = escopo.ServiceProvider;

        var banco = provedor.GetRequiredService<NexoDbContext>();
        var registro = provedor.GetRequiredService<ILoggerFactory>().CreateLogger("Migração");

        await banco.Database.OpenConnectionAsync(cancelamento);

        try
        {
            await banco.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_lock({0})", [Trinco], cancelamento);

            var pendentes = (await banco.Database.GetPendingMigrationsAsync(cancelamento)).ToList();

            if (pendentes.Count == 0)
            {
                registro.LogInformation("Banco já está na versão desta aplicação.");
                return;
            }

            registro.LogInformation(
                "Aplicando {Quantidade} migração(ões): {Migracoes}",
                pendentes.Count, string.Join(", ", pendentes));

            await banco.Database.MigrateAsync(cancelamento);

            registro.LogInformation("Migrações aplicadas.");
        }
        finally
        {
            /*
             * Soltar o trinco antes de devolver a conexão. Fechar a conexão já
             * o soltaria, mas depender disso deixa o comportamento na mão do
             * pool — e trinco preso trava a próxima implantação inteira.
             */
            await banco.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock({0})", [Trinco], CancellationToken.None);
            await banco.Database.CloseConnectionAsync();
        }
    }
}
