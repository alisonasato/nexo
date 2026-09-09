using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Nexo.Api.Dados;

/// <summary>
/// O ponto único onde o tenant chega ao Postgres (decisão Q28).
///
/// Toda vez que uma conexão é aberta — e <b>toda</b> vez, sem condição —, este
/// interceptor grava <c>app.tenant_id</c> na sessão. É desse valor que as
/// políticas de RLS leem quem está perguntando.
///
/// Por que gravar na sessão em vez de <c>SET LOCAL</c> dentro da transação:
/// o perigo do <c>SET LOCAL</c> ser dispensado é o pool entregar uma conexão
/// ainda marcada com o tenant da requisição anterior. Esse risco morre quando
/// a gravação acontece em toda abertura, incondicionalmente: não existe
/// conexão emprestada sem passar por aqui. O Npgsql ainda emite
/// <c>DISCARD ALL</c> ao devolver a conexão ao pool, o que limpa a
/// configuração de qualquer jeito — são duas defesas, não uma.
///
/// Quando não há tenant, grava string vazia em vez de não gravar nada. A
/// política usa <c>nullif(..., '')</c> e a comparação vira nula, que em SQL não
/// é verdadeira: sem tenant, nenhuma linha.
///
/// Uma ressalva: isto pressupõe o pool normal do Npgsql. Com multiplexação
/// ligada, uma sessão deixa de pertencer a uma conexão lógica e este desenho
/// não vale mais.
/// </summary>
public sealed class InterceptorDeTenant(IContextoDeTenant contexto) : DbConnectionInterceptor
{
    private const string Comando = "SELECT set_config('app.tenant_id', $1, false)";

    public override void ConnectionOpened(DbConnection conexao, ConnectionEndEventData dados)
        => Aplicar(conexao);

    public override async Task ConnectionOpenedAsync(
        DbConnection conexao,
        ConnectionEndEventData dados,
        CancellationToken cancelamento = default)
        => await AplicarAsync(conexao, cancelamento);

    private void Aplicar(DbConnection conexao)
    {
        using var comando = Preparar(conexao);
        comando.ExecuteNonQuery();
    }

    private async Task AplicarAsync(DbConnection conexao, CancellationToken cancelamento)
    {
        await using var comando = Preparar(conexao);
        await comando.ExecuteNonQueryAsync(cancelamento);
    }

    private NpgsqlCommand Preparar(DbConnection conexao)
    {
        var comando = (NpgsqlCommand)conexao.CreateCommand();
        comando.CommandText = Comando;
        comando.Parameters.AddWithValue(contexto.TenantAtual?.ToString() ?? string.Empty);
        return comando;
    }
}
