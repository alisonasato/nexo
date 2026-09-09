using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Api.Dados.Migracoes;

/// <summary>
/// A política de isolamento entre tenants (decisão Q22).
///
/// Ela vive no banco, e não no C#, de propósito: um <c>WHERE</c> esquecido em
/// uma consulta nova é uma questão de tempo, e a política no banco recusa a
/// linha mesmo quando a aplicação esquece de pedir.
///
/// Três detalhes que, se faltarem, fazem tudo funcionar — inclusive ver o dado
/// do vizinho:
///
/// 1. <c>FORCE ROW LEVEL SECURITY</c>. Sem ele, o dono da tabela ignora a
///    política, e em desenvolvimento a aplicação normalmente é a dona. Em
///    produção o certo é a aplicação conectar com um papel que não seja dono
///    nem tenha <c>BYPASSRLS</c>; o FORCE é a defesa que sobra quando esse
///    cuidado falha.
/// 2. <c>current_setting('app.tenant_id', true)</c> com o segundo argumento
///    verdadeiro: sem ele, a consulta explode quando a configuração não existe,
///    em vez de simplesmente não devolver nada.
/// 3. <c>nullif(..., '')</c>. O interceptor grava string vazia quando não há
///    tenant, e <c>''::uuid</c> é erro de conversão. Com o nullif, vira nulo, a
///    comparação vira nula, e nulo não é verdadeiro: sem tenant, nenhuma linha.
///    A falha é fechada.
///
/// O <c>WITH CHECK</c> cuida do outro lado: além de não ler a linha alheia,
/// ninguém grava linha carimbada com o tenant de outro.
/// </summary>
public partial class SegurancaPorLinha : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE empresas ENABLE ROW LEVEL SECURITY;");
        migrationBuilder.Sql("ALTER TABLE empresas FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql("""
            CREATE POLICY empresas_do_tenant ON empresas
                USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP POLICY IF EXISTS empresas_do_tenant ON empresas;");
        migrationBuilder.Sql("ALTER TABLE empresas NO FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql("ALTER TABLE empresas DISABLE ROW LEVEL SECURITY;");
    }
}
