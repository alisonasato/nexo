using Npgsql;

namespace Nexo.Api.Dados;

/// <summary>
/// Descobre a string de conexão, aceitando as duas formas que aparecem na vida
/// real.
///
/// <para>
/// O Npgsql espera pares — <c>Host=…;Port=…;Database=…</c> —, mas quase todo
/// PaaS entrega uma URI: <c>postgresql://usuario:senha@servidor:5432/banco</c>.
/// Sem isto, implantar exige converter à mão, e conversão à mão na hora do
/// primeiro deploy é onde se perde uma tarde: o serviço sobe, responde
/// <c>/saude</c> normalmente, e só quebra na primeira consulta.
/// </para>
/// <para>
/// A ordem de procura também importa. <c>ConnectionStrings:Nexo</c> vem
/// primeiro porque é a configuração explícita de quem sabe o que quer;
/// <c>DATABASE_URL</c> é o nome que os PaaS injetam sozinhos ao ligar um banco
/// ao serviço, e serve de padrão para não precisar copiar nada.
/// </para>
/// </summary>
public static class ConexaoDoBanco
{
    public const string Nome = "Nexo";

    public static string Resolver(IConfiguration configuracao)
    {
        var explicita = configuracao.GetConnectionString(Nome);
        if (!string.IsNullOrWhiteSpace(explicita)) return Normalizar(explicita);

        var doAmbiente = configuracao["DATABASE_URL"];
        if (!string.IsNullOrWhiteSpace(doAmbiente)) return Normalizar(doAmbiente);

        throw new InvalidOperationException(
            $"Configure ConnectionStrings__{Nome} ou DATABASE_URL. " +
            "Aceita tanto a forma do Npgsql (Host=…;Port=…;Database=…;Username=…;Password=…) " +
            "quanto a URI que os PaaS entregam (postgresql://usuario:senha@servidor:5432/banco).");
    }

    /// <summary>
    /// Converte a URI em string do Npgsql. Se já vier na forma de pares,
    /// devolve como está — não há o que adivinhar.
    /// </summary>
    public static string Normalizar(string valor)
    {
        var texto = valor.Trim();

        var ehUri = texto.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || texto.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

        if (!ehUri) return texto;

        var uri = new Uri(texto);

        var construtor = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
        };

        /*
         * Usuário e senha vêm percent-encoded na URI. Senha gerada por PaaS
         * costuma ter `@`, `/` e `+`, que é exatamente o que quebra se alguém
         * copiar o pedaço sem decodificar.
         */
        var credencial = uri.UserInfo.Split(':', 2);
        if (credencial.Length > 0 && credencial[0].Length > 0)
            construtor.Username = Uri.UnescapeDataString(credencial[0]);
        if (credencial.Length > 1)
            construtor.Password = Uri.UnescapeDataString(credencial[1]);

        /* `?sslmode=require` é comum quando o banco é acessado de fora da rede interna. */
        foreach (var parte in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var par = parte.Split('=', 2);
            if (par.Length != 2) continue;

            if (par[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<SslMode>(par[1], ignoreCase: true, out var modo))
            {
                construtor.SslMode = modo;
            }
        }

        return construtor.ConnectionString;
    }
}
