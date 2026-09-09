using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Api.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Pessoas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pessoas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    nome_fantasia = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    documento = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    inscricao_estadual = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    inscricao_municipal = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    celular = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    endereco_cep = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    endereco_logradouro = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    endereco_numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    endereco_complemento = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    endereco_bairro = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    endereco_cidade = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    endereco_uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    observacoes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pessoas", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pessoas_tenant_id_documento",
                table: "pessoas",
                columns: new[] { "tenant_id", "documento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pessoas_tenant_id_nome",
                table: "pessoas",
                columns: new[] { "tenant_id", "nome" });

            /* A tabela nasce isolada. Ver IsolamentoPorTenant. */
            migrationBuilder.AplicarIsolamentoPorTenant("pessoas");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoverIsolamentoPorTenant("pessoas");

            migrationBuilder.DropTable(
                name: "pessoas");
        }
    }
}
