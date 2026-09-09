using Microsoft.EntityFrameworkCore.Migrations;

namespace Nexo.Api.Dados.Migracoes;

/// <summary>
/// Liga a política de isolamento numa tabela que tem <c>tenant_id</c>.
///
/// Existe para que a política ande junto com a tabela, na mesma migração. A
/// alternativa — lembrar de escrever o SQL toda vez — é a forma mais provável
/// de o isolamento falhar: não por estar errado, mas por não estar lá.
///
/// Os três detalhes que fazem a política valer alguma coisa estão explicados
/// em <see cref="SegurancaPorLinha"/>, e são os mesmos aqui: <c>FORCE</c>
/// porque a aplicação costuma ser dona da tabela, <c>current_setting</c> com
/// o segundo argumento verdadeiro para não explodir quando não há tenant, e
/// <c>nullif</c> porque o interceptor grava string vazia nesse caso — e a
/// comparação com nulo não é verdadeira, então sem tenant não vem nada.
/// </summary>
public static class IsolamentoPorTenant
{
    public static void AplicarIsolamentoPorTenant(this MigrationBuilder migracao, string tabela)
    {
        var politica = tabela + "_do_tenant";

        migracao.Sql($"ALTER TABLE {tabela} ENABLE ROW LEVEL SECURITY;");
        migracao.Sql($"ALTER TABLE {tabela} FORCE ROW LEVEL SECURITY;");
        migracao.Sql($"""
            CREATE POLICY {politica} ON {tabela}
                USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
            """);
    }

    public static void RemoverIsolamentoPorTenant(this MigrationBuilder migracao, string tabela)
    {
        migracao.Sql($"DROP POLICY IF EXISTS {tabela}_do_tenant ON {tabela};");
        migracao.Sql($"ALTER TABLE {tabela} NO FORCE ROW LEVEL SECURITY;");
        migracao.Sql($"ALTER TABLE {tabela} DISABLE ROW LEVEL SECURITY;");
    }
}
