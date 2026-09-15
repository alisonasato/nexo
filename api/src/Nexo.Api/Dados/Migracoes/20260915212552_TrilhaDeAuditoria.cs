using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Api.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class TrilhaDeAuditoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "eventos_de_auditoria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entidade = table.Column<int>(type: "integer", nullable: false),
                    entidade_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lancamento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    conta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acao = table.Column<int>(type: "integer", nullable: false),
                    mudancas = table.Column<string>(type: "jsonb", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    autor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_de_auditoria", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_eventos_de_auditoria_tenant_id_conta_id_em",
                table: "eventos_de_auditoria",
                columns: new[] { "tenant_id", "conta_id", "em" });

            migrationBuilder.CreateIndex(
                name: "ix_eventos_de_auditoria_tenant_id_lancamento_id_em",
                table: "eventos_de_auditoria",
                columns: new[] { "tenant_id", "lancamento_id", "em" });

            /* O EF não escreve política de isolamento: sem esta linha, a trilha de um escritório seria legível por todos. */
            migrationBuilder.AplicarIsolamentoPorTenant("eventos_de_auditoria");

            /*
             * A trilha só recebe eventos novos. A política decide que linhas cada
             * escritório enxerga; o que se pode fazer com elas é o gatilho que diz.
             * Ele é por comando, e não por linha: recusa também o UPDATE e o DELETE
             * que não achariam nada, e o TRUNCATE, que nem passa por linha.
             *
             * Apagar a trilha de verdade, como numa exclusão pedida pela LGPD, é
             * gesto de quem administra o banco: desliga o gatilho, apaga, liga de novo.
             */
            migrationBuilder.Sql("""
                CREATE FUNCTION recusar_reescrita_da_trilha() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'A trilha de auditoria só recebe eventos novos: % recusado.', TG_OP;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trilha_so_recebe_eventos_novos
                    BEFORE UPDATE OR DELETE OR TRUNCATE ON eventos_de_auditoria
                    FOR EACH STATEMENT EXECUTE FUNCTION recusar_reescrita_da_trilha();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trilha_so_recebe_eventos_novos ON eventos_de_auditoria;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS recusar_reescrita_da_trilha();");
            migrationBuilder.RemoverIsolamentoPorTenant("eventos_de_auditoria");

            migrationBuilder.DropTable(
                name: "eventos_de_auditoria");
        }
    }
}
