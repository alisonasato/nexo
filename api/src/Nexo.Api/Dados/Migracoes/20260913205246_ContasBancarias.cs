using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Api.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class ContasBancarias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contas_bancarias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    banco = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    agencia = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    saldo_inicial = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    saldo_inicial_em = table.Column<DateOnly>(type: "date", nullable: false),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contas_bancarias", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contas_bancarias_tenant_id_nome",
                table: "contas_bancarias",
                columns: new[] { "tenant_id", "nome" },
                unique: true);

            /* O EF não escreve política de isolamento: sem esta linha, a tabela nasceria legível por todos os escritórios. */
            migrationBuilder.AplicarIsolamentoPorTenant("contas_bancarias");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoverIsolamentoPorTenant("contas_bancarias");

            migrationBuilder.DropTable(
                name: "contas_bancarias");
        }
    }
}
