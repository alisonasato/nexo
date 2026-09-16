using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Api.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Recorrencias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "recorrencia_id",
                table: "lancamentos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recorrencias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    natureza = table.Column<int>(type: "integer", nullable: false),
                    pessoa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    valor = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    frequencia = table.Column<int>(type: "integer", nullable: false),
                    dia_de_vencimento = table.Column<int>(type: "integer", nullable: false),
                    inicio_em = table.Column<DateOnly>(type: "date", nullable: false),
                    fim_em = table.Column<DateOnly>(type: "date", nullable: true),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    categoria_id = table.Column<Guid>(type: "uuid", nullable: true),
                    centro_de_custo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recorrencias", x => x.id);
                    table.ForeignKey(
                        name: "fk_recorrencias_categorias_categoria_id",
                        column: x => x.categoria_id,
                        principalTable: "categorias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recorrencias_centros_de_custo_centro_de_custo_id",
                        column: x => x.centro_de_custo_id,
                        principalTable: "centros_de_custo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recorrencias_pessoas_pessoa_id",
                        column: x => x.pessoa_id,
                        principalTable: "pessoas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_recorrencia_id",
                table: "lancamentos",
                column: "recorrencia_id");

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_tenant_id_recorrencia_id_competencia_ano_compet",
                table: "lancamentos",
                columns: new[] { "tenant_id", "recorrencia_id", "competencia_ano", "competencia_mes" },
                unique: true,
                filter: "situacao <> 3");

            migrationBuilder.CreateIndex(
                name: "ix_recorrencias_categoria_id",
                table: "recorrencias",
                column: "categoria_id");

            migrationBuilder.CreateIndex(
                name: "ix_recorrencias_centro_de_custo_id",
                table: "recorrencias",
                column: "centro_de_custo_id");

            migrationBuilder.CreateIndex(
                name: "ix_recorrencias_pessoa_id",
                table: "recorrencias",
                column: "pessoa_id");

            migrationBuilder.CreateIndex(
                name: "ix_recorrencias_tenant_id_natureza_ativa",
                table: "recorrencias",
                columns: new[] { "tenant_id", "natureza", "ativa" });

            migrationBuilder.AddForeignKey(
                name: "fk_lancamentos_recorrencias_recorrencia_id",
                table: "lancamentos",
                column: "recorrencia_id",
                principalTable: "recorrencias",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            /* O EF não escreve política de isolamento: sem esta linha, as recorrências de um escritório seriam legíveis por todos. */
            migrationBuilder.AplicarIsolamentoPorTenant("recorrencias");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoverIsolamentoPorTenant("recorrencias");

            migrationBuilder.DropForeignKey(
                name: "fk_lancamentos_recorrencias_recorrencia_id",
                table: "lancamentos");

            migrationBuilder.DropTable(
                name: "recorrencias");

            migrationBuilder.DropIndex(
                name: "ix_lancamentos_recorrencia_id",
                table: "lancamentos");

            migrationBuilder.DropIndex(
                name: "ix_lancamentos_tenant_id_recorrencia_id_competencia_ano_compet",
                table: "lancamentos");

            migrationBuilder.DropColumn(
                name: "recorrencia_id",
                table: "lancamentos");
        }
    }
}
