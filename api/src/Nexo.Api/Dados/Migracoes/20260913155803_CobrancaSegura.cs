using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Api.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class CobrancaSegura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "divergencia",
                table: "eventos_de_cobranca",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "divergencia",
                table: "eventos_de_cobranca");
        }
    }
}
