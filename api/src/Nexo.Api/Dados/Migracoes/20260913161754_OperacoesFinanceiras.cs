using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Api.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class OperacoesFinanceiras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "parcela_numero",
                table: "recebiveis",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parcelamento_id",
                table: "recebiveis",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "parcelas_total",
                table: "recebiveis",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "renegociado_de_id",
                table: "recebiveis",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "renegociacoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origem_id = table.Column<Guid>(type: "uuid", nullable: false),
                    juros = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    multa = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    desconto = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    motivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_renegociacoes", x => x.id);
                    table.ForeignKey(
                        name: "fk_renegociacoes_recebiveis_origem_id",
                        column: x => x.origem_id,
                        principalTable: "recebiveis",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recebiveis_renegociado_de_id",
                table: "recebiveis",
                column: "renegociado_de_id");

            migrationBuilder.CreateIndex(
                name: "ix_recebiveis_tenant_id_parcelamento_id",
                table: "recebiveis",
                columns: new[] { "tenant_id", "parcelamento_id" });

            migrationBuilder.CreateIndex(
                name: "ix_renegociacoes_origem_id",
                table: "renegociacoes",
                column: "origem_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_renegociacoes_tenant_id",
                table: "renegociacoes",
                column: "tenant_id");

            migrationBuilder.AddForeignKey(
                name: "fk_recebiveis_recebiveis_renegociado_de_id",
                table: "recebiveis",
                column: "renegociado_de_id",
                principalTable: "recebiveis",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            /*
             * A política anda junto com a tabela, na mesma migração. O gerador
             * do EF não sabe dela, e tabela nova sem política é o jeito mais
             * provável de o isolamento falhar: não por estar errado, mas por
             * não estar lá.
             */
            migrationBuilder.AplicarIsolamentoPorTenant("renegociacoes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_recebiveis_recebiveis_renegociado_de_id",
                table: "recebiveis");

            migrationBuilder.RemoverIsolamentoPorTenant("renegociacoes");

            migrationBuilder.DropTable(
                name: "renegociacoes");

            migrationBuilder.DropIndex(
                name: "ix_recebiveis_renegociado_de_id",
                table: "recebiveis");

            migrationBuilder.DropIndex(
                name: "ix_recebiveis_tenant_id_parcelamento_id",
                table: "recebiveis");

            migrationBuilder.DropColumn(
                name: "parcela_numero",
                table: "recebiveis");

            migrationBuilder.DropColumn(
                name: "parcelamento_id",
                table: "recebiveis");

            migrationBuilder.DropColumn(
                name: "parcelas_total",
                table: "recebiveis");

            migrationBuilder.DropColumn(
                name: "renegociado_de_id",
                table: "recebiveis");
        }
    }
}
