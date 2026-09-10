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
            "quanto a URI que os PaaS entregam (postgresql://usuario:senha@servidor:5432/banco). " +
            DescreverAmbiente());
    }

    /// <summary>
    /// Lista os <b>nomes</b> das variáveis de ambiente que parecem ser de banco.
    ///
    /// <para>
    /// Existe porque cada tentativa de implantação custa caro: sem isto, o erro
    /// só diz o que falta, e não o que chegou. Ver que o ambiente tem
    /// <c>PGHOST</c> e <c>DATABASE_PUBLIC_URL</c> mas não <c>DATABASE_URL</c>
    /// responde a pergunta na hora, em vez de custar mais uma implantação.
    /// </para>
    /// <para>
    /// <b>Só nomes, nunca valores.</b> O valor carrega a senha do banco, e uma
    /// mensagem de erro vai para o log — que é o último lugar onde ela deve
    /// aparecer.
    /// </para>
    /// </summary>
    private static string DescreverAmbiente()
    {
        string[] pistas = ["DATABASE", "POSTGRES", "PG", "CONNECTIONSTRINGS"];

        var encontradas = Environment.GetEnvironmentVariables()
            .Keys
            .Cast<object>()
            .Select(chave => chave?.ToString() ?? string.Empty)
            .Where(nome => pistas.Any(pista => nome.ToUpperInvariant().Contains(pista)))
            .OrderBy(nome => nome, StringComparer.Ordinal)
            .ToList();

        return encontradas.Count == 0
            ? "Nenhuma variável de ambiente com cara de banco chegou até aqui — nem uma sequer."
            : "Variáveis de banco presentes no ambiente (só os nomes; valores omitidos porque " +
              $"contêm senha): {string.Join(", ", encontradas)}.";
    }

    /// <summary>
    /// Converte a URI em string do Npgsql. Se já vier na forma de pares,
    /// devolve como está — não há o que adivinhar.
    /// </summary>
    public static string Normalizar(string valor)
    {
        /*
         * Aspas coladas por engano ao copiar de um painel são erro comum, e
         * fazem o driver recusar a string inteira sem dizer por quê.
         */
        var texto = valor.Trim().Trim('"', '\'');

        /*
         * Referência não resolvida.
         *
         * No PaaS, apontar uma variável para outro serviço é escrever algo como
         * ${{Postgres.DATABASE_URL}}. Se o nome do serviço estiver errado, o
         * texto chega literal — e sem esta checagem ele seguiria adiante até o
         * driver estourar com "Format of the initialization string does not
         * conform to specification starting at index 0", que não diz nada a
         * quem está tentando implantar.
         */
        if (texto.StartsWith("${{", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A variável de conexão chegou como uma referência não resolvida. " +
                "Isso acontece quando o nome do serviço na referência não existe. " +
                "Confira o nome exato do serviço de banco no painel e ajuste — por " +
                "exemplo ${{Postgres.DATABASE_URL}}, com Postgres trocado pelo nome real.");
        }

        var ehUri = texto.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || texto.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

        if (!ehUri)
        {
            /*
             * Nem URI nem pares chave=valor. Descrever sem citar: o valor traz
             * a senha do banco, e log é o último lugar onde ela deve aparecer.
             */
            if (!texto.Contains('='))
            {
                throw new InvalidOperationException(
                    $"A variável de conexão não está em nenhum formato reconhecido: tem " +
                    $"{texto.Length} caracteres, não contém '=' e não começa com " +
                    "'postgresql://'. Esperado ou 'Host=...;Port=...;Database=...' ou " +
                    "'postgresql://usuario:senha@servidor:5432/banco'. O conteúdo não é " +
                    "registrado aqui porque contém senha.");
            }

            return texto;
        }

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
