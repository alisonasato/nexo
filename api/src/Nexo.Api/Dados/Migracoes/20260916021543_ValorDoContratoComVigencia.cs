using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Api.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class ValorDoContratoComVigencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "valores_de_contrato",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valor = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    vigente_de_ano = table.Column<int>(type: "integer", nullable: false),
                    vigente_de_mes = table.Column<int>(type: "integer", nullable: false),
                    motivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    percentual = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_valores_de_contrato", x => x.id);
                    table.ForeignKey(
                        name: "fk_valores_de_contrato_contratos_contrato_id",
                        column: x => x.contrato_id,
                        principalTable: "contratos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_valores_de_contrato_contrato_id",
                table: "valores_de_contrato",
                column: "contrato_id");

            migrationBuilder.CreateIndex(
                name: "ix_valores_de_contrato_tenant_id_contrato_id_vigente_de_ano_vi",
                table: "valores_de_contrato",
                columns: new[] { "tenant_id", "contrato_id", "vigente_de_ano", "vigente_de_mes" },
                unique: true);

            /*
             * O valor que está em cada contrato vira a primeira vigência, começando
             * na competência do início dele.
             *
             * A ordem aqui é a única que funciona, e o EF escreveu outra: copiar
             * antes de a coluna sair, e antes de a política de isolamento entrar. A
             * migração roda sem tenant nenhum — com a política ligada, o WITH CHECK
             * recusaria cada linha, e o histórico nasceria vazio sem erro nenhum.
             */
            migrationBuilder.Sql("""
                INSERT INTO valores_de_contrato
                    (id, tenant_id, contrato_id, valor, vigente_de_ano, vigente_de_mes, motivo, criado_em)
                SELECT gen_random_uuid(), tenant_id, id, valor,
                       EXTRACT(YEAR FROM inicio_da_vigencia)::int,
                       EXTRACT(MONTH FROM inicio_da_vigencia)::int,
                       'Valor inicial', now()
                FROM contratos;
                """);

            migrationBuilder.AplicarIsolamentoPorTenant("valores_de_contrato");

            migrationBuilder.DropColumn(
                name: "valor",
                table: "contratos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "valor",
                table: "contratos",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            /* A política sai antes da leitura de volta: sem tenant, ela não enxergaria linha nenhuma. */
            migrationBuilder.RemoverIsolamentoPorTenant("valores_de_contrato");

            /* Volta para o contrato o valor da última vigência, que é o que a coluna guardava. */
            migrationBuilder.Sql("""
                UPDATE contratos
                SET valor = COALESCE((
                    SELECT v.valor
                    FROM valores_de_contrato v
                    WHERE v.contrato_id = contratos.id
                    ORDER BY v.vigente_de_ano DESC, v.vigente_de_mes DESC
                    LIMIT 1), 0);
                """);

            migrationBuilder.DropTable(
                name: "valores_de_contrato");
        }
    }
}
