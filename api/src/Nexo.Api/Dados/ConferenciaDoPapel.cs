using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Nexo.Api.Dados;

/// <summary>
/// Confere, na subida, que o papel do banco não atravessa a política de
/// isolamento.
///
/// <para>
/// <b>Superusuário do Postgres ignora RLS por completo</b> — inclusive com
/// <c>FORCE ROW LEVEL SECURITY</c>. O mesmo vale para um papel com
/// <c>BYPASSRLS</c>. Com qualquer um dos dois, o isolamento entre escritórios
/// simplesmente não existe: toda consulta enxerga todos os tenants, e
/// <b>nada acusa</b>. A aplicação sobe saudável, as telas funcionam, e um
/// cliente vê o dado do outro.
/// </para>
/// <para>
/// É o único erro desta implantação sem sintoma, e por isso é o único que vale
/// conferir sozinho na subida. Deixar isso na forma de "lembre-se de rodar esta
/// consulta depois" é o mesmo que não ter proteção nenhuma — a lembrança falha
/// exatamente quando o sistema cresce e passa a importar.
/// </para>
/// <para>
/// Em produção a aplicação <b>se recusa a subir</b>. Fora dela, apenas registra
/// um aviso: quem desenvolve às vezes aponta para um banco administrativo de
/// propósito, e derrubar o ambiente local por isso atrapalharia sem proteger
/// ninguém. Falhar fechado onde há dado de gente é a mesma regra que vale para
/// a chave de assinatura e para o tenant ausente.
/// </para>
/// </summary>
public static class ConferenciaDoPapel
{
    public static async Task VerificarAsync(
        IServiceProvider servicos,
        IHostEnvironment ambiente,
        CancellationToken cancelamento = default)
    {
        using var escopo = servicos.CreateScope();
        var provedor = escopo.ServiceProvider;

        var banco = provedor.GetRequiredService<NexoDbContext>();
        var registro = provedor.GetRequiredService<ILoggerFactory>().CreateLogger("Papel do banco");

        await banco.Database.OpenConnectionAsync(cancelamento);

        try
        {
            var conexao = (NpgsqlConnection)banco.Database.GetDbConnection();

            await using var comando = conexao.CreateCommand();
            comando.CommandText =
                "SELECT rolname, rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user";

            await using var leitor = await comando.ExecuteReaderAsync(cancelamento);

            if (!await leitor.ReadAsync(cancelamento))
            {
                /* Não deveria acontecer: todo usuário conectado existe em pg_roles. */
                registro.LogWarning("Não foi possível identificar o papel do banco para conferir o isolamento.");
                return;
            }

            var papel = leitor.GetString(0);
            var superusuario = leitor.GetBoolean(1);
            var ignoraRls = leitor.GetBoolean(2);

            if (!superusuario && !ignoraRls)
            {
                registro.LogInformation(
                    "Papel {Papel} confere: não é superusuário e não ignora RLS.", papel);
                return;
            }

            var motivo = superusuario && ignoraRls ? "é superusuário e tem BYPASSRLS"
                : superusuario ? "é superusuário"
                : "tem BYPASSRLS";

            var recado =
                $"O papel {papel} {motivo}, e por isso atravessa a política de isolamento entre " +
                "tenants mesmo com FORCE ROW LEVEL SECURITY. Nesse estado um escritório enxerga " +
                "o dado do outro, e nada no sistema acusa. Crie um papel comum (NOSUPERUSER " +
                "NOBYPASSRLS), dê a ele posse das tabelas, e use-o na string de conexão.";

            /*
             * A saída consciente.
             *
             * O banco que um PaaS entrega costuma vir com um papel
             * administrativo, e recusar a subida derrubaria uma instalação que
             * já funciona — o que empurraria alguém a desligar a conferência
             * inteira, que é o pior desfecho.
             *
             * Então há uma porta, e ela é estreita de propósito: o nome da
             * variável diz o que está sendo aceito, e o log repete o aviso a
             * cada subida. Enquanto houver um tenant só não há o que vazar; com
             * o segundo, isto vira dívida com prazo.
             */
            const string aceite = "NEXO_ACEITO_SEM_ISOLAMENTO";

            if (Environment.GetEnvironmentVariable(aceite) == "1")
            {
                registro.LogWarning(
                    "{Recado} Subindo assim mesmo porque {Variavel} está ligada — o isolamento " +
                    "entre tenants NÃO está valendo neste banco.", recado, aceite);
                return;
            }

            if (ambiente.IsProduction())
            {
                throw new InvalidOperationException(
                    recado + $" Se você precisa subir agora e há um tenant só, defina {aceite}=1 " +
                    "para aceitar conscientemente rodar sem isolamento — e trate isso como dívida.");
            }

            registro.LogWarning("{Recado}", recado);
        }
        finally
        {
            await banco.Database.CloseConnectionAsync();
        }
    }
}
