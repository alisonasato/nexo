using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Api.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class CancelamentoDeRecebivel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_recebiveis_tenant_id_contrato_id_competencia_ano_competenci",
                table: "recebiveis");

            migrationBuilder.AddColumn<string>(
                name: "motivo_do_cancelamento",
                table: "recebiveis",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_recebiveis_tenant_id_contrato_id_competencia_ano_competenci",
                table: "recebiveis",
                columns: new[] { "tenant_id", "contrato_id", "competencia_ano", "competencia_mes" },
                unique: true,
                filter: "situacao <> 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_recebiveis_tenant_id_contrato_id_competencia_ano_competenci",
                table: "recebiveis");

            migrationBuilder.DropColumn(
                name: "motivo_do_cancelamento",
                table: "recebiveis");

            migrationBuilder.CreateIndex(
                name: "ix_recebiveis_tenant_id_contrato_id_competencia_ano_competenci",
                table: "recebiveis",
                columns: new[] { "tenant_id", "contrato_id", "competencia_ano", "competencia_mes" },
                unique: true);
        }
    }
}
