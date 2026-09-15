using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Api.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class PlanoDeContas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "categorias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    natureza = table.Column<int>(type: "integer", nullable: false),
                    pai_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categorias", x => x.id);
                    table.ForeignKey(
                        name: "fk_categorias_categorias_pai_id",
                        column: x => x.pai_id,
                        principalTable: "categorias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "centros_de_custo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_centros_de_custo", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_categorias_pai_id",
                table: "categorias",
                column: "pai_id");

            migrationBuilder.CreateIndex(
                name: "ix_categorias_tenant_id_pai_id",
                table: "categorias",
                columns: new[] { "tenant_id", "pai_id" });

            migrationBuilder.CreateIndex(
                name: "ix_centros_de_custo_tenant_id_nome",
                table: "centros_de_custo",
                columns: new[] { "tenant_id", "nome" },
                unique: true);

            /* O EF não escreve política de isolamento: sem estas linhas, o plano de um escritório seria legível por todos. */
            migrationBuilder.AplicarIsolamentoPorTenant("categorias");
            migrationBuilder.AplicarIsolamentoPorTenant("centros_de_custo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoverIsolamentoPorTenant("categorias");
            migrationBuilder.RemoverIsolamentoPorTenant("centros_de_custo");

            migrationBuilder.DropTable(
                name: "categorias");

            migrationBuilder.DropTable(
                name: "centros_de_custo");
        }
    }
}
