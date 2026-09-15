using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Api.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class MovimentosDasContas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "recebe_cobrancas",
                table: "contas_bancarias",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "movimentos_de_conta",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lancamento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    valor = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    origem = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movimentos_de_conta", x => x.id);
                    table.ForeignKey(
                        name: "fk_movimentos_de_conta_contas_bancarias_conta_id",
                        column: x => x.conta_id,
                        principalTable: "contas_bancarias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movimentos_de_conta_lancamentos_lancamento_id",
                        column: x => x.lancamento_id,
                        principalTable: "lancamentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contas_bancarias_tenant_id",
                table: "contas_bancarias",
                column: "tenant_id",
                unique: true,
                filter: "recebe_cobrancas");

            migrationBuilder.CreateIndex(
                name: "ix_movimentos_de_conta_conta_id",
                table: "movimentos_de_conta",
                column: "conta_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimentos_de_conta_lancamento_id",
                table: "movimentos_de_conta",
                column: "lancamento_id",
                unique: true,
                filter: "lancamento_id IS NOT NULL AND origem = 'baixa'");

            migrationBuilder.CreateIndex(
                name: "ix_movimentos_de_conta_tenant_id_conta_id_data",
                table: "movimentos_de_conta",
                columns: new[] { "tenant_id", "conta_id", "data" });

            /* O EF não escreve política de isolamento: sem esta linha, os movimentos de um escritório seriam legíveis por todos. */
            migrationBuilder.AplicarIsolamentoPorTenant("movimentos_de_conta");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoverIsolamentoPorTenant("movimentos_de_conta");

            migrationBuilder.DropTable(
                name: "movimentos_de_conta");

            migrationBuilder.DropIndex(
                name: "ix_contas_bancarias_tenant_id",
                table: "contas_bancarias");

            migrationBuilder.DropColumn(
                name: "recebe_cobrancas",
                table: "contas_bancarias");
        }
    }
}
