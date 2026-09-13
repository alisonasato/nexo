using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Api.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class TransferenciasEAvulsos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "transferencia_id",
                table: "movimentos_de_conta",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_movimentos_de_conta_transferencia_id",
                table: "movimentos_de_conta",
                column: "transferencia_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_movimentos_de_conta_transferencia_id",
                table: "movimentos_de_conta");

            migrationBuilder.DropColumn(
                name: "transferencia_id",
                table: "movimentos_de_conta");
        }
    }
}
