using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Api.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class CobrancaPeloPsp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cobranca_id",
                table: "recebiveis",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "cobranca_url",
                table: "recebiveis",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "cliente_no_asaas",
                table: "pessoas",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "eventos_de_cobranca",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    recebivel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recebido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_de_cobranca", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_eventos_de_cobranca_tenant_id_recebivel_id",
                table: "eventos_de_cobranca",
                columns: new[] { "tenant_id", "recebivel_id" });

            /*
             * A política anda junto com a tabela, na mesma migração. O gerador
             * do EF não sabe dela, e tabela nova sem política é o jeito mais
             * provável de o isolamento falhar: não por estar errado, mas por
             * não estar lá. Esta em particular é escrita por uma requisição
             * anônima, o que faz dela o pior lugar para esquecer.
             */
            migrationBuilder.AplicarIsolamentoPorTenant("eventos_de_cobranca");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoverIsolamentoPorTenant("eventos_de_cobranca");

            migrationBuilder.DropTable(
                name: "eventos_de_cobranca");

            migrationBuilder.DropColumn(
                name: "cobranca_id",
                table: "recebiveis");

            migrationBuilder.DropColumn(
                name: "cobranca_url",
                table: "recebiveis");

            migrationBuilder.DropColumn(
                name: "cliente_no_asaas",
                table: "pessoas");
        }
    }
}
