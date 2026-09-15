using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Api.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class LancamentoClassificado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "categoria_id",
                table: "lancamentos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "centro_de_custo_id",
                table: "lancamentos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_categoria_id",
                table: "lancamentos",
                column: "categoria_id");

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_centro_de_custo_id",
                table: "lancamentos",
                column: "centro_de_custo_id");

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_tenant_id_categoria_id",
                table: "lancamentos",
                columns: new[] { "tenant_id", "categoria_id" });

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_tenant_id_centro_de_custo_id",
                table: "lancamentos",
                columns: new[] { "tenant_id", "centro_de_custo_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_lancamentos_categorias_categoria_id",
                table: "lancamentos",
                column: "categoria_id",
                principalTable: "categorias",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_lancamentos_centros_de_custo_centro_de_custo_id",
                table: "lancamentos",
                column: "centro_de_custo_id",
                principalTable: "centros_de_custo",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_lancamentos_categorias_categoria_id",
                table: "lancamentos");

            migrationBuilder.DropForeignKey(
                name: "fk_lancamentos_centros_de_custo_centro_de_custo_id",
                table: "lancamentos");

            migrationBuilder.DropIndex(
                name: "ix_lancamentos_categoria_id",
                table: "lancamentos");

            migrationBuilder.DropIndex(
                name: "ix_lancamentos_centro_de_custo_id",
                table: "lancamentos");

            migrationBuilder.DropIndex(
                name: "ix_lancamentos_tenant_id_categoria_id",
                table: "lancamentos");

            migrationBuilder.DropIndex(
                name: "ix_lancamentos_tenant_id_centro_de_custo_id",
                table: "lancamentos");

            migrationBuilder.DropColumn(
                name: "categoria_id",
                table: "lancamentos");

            migrationBuilder.DropColumn(
                name: "centro_de_custo_id",
                table: "lancamentos");
        }
    }
}
