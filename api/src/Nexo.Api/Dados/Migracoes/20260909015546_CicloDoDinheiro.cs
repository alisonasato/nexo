using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Api.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class CicloDoDinheiro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clientes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pessoa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    regime_tributario = table.Column<int>(type: "integer", nullable: false),
                    responsavel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    observacoes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clientes", x => x.id);
                    table.ForeignKey(
                        name: "fk_clientes_pessoas_pessoa_id",
                        column: x => x.pessoa_id,
                        principalTable: "pessoas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contratos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    valor = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    dia_de_vencimento = table.Column<int>(type: "integer", nullable: false),
                    inicio_da_vigencia = table.Column<DateOnly>(type: "date", nullable: false),
                    fim_da_vigencia = table.Column<DateOnly>(type: "date", nullable: true),
                    situacao = table.Column<int>(type: "integer", nullable: false),
                    observacoes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contratos", x => x.id);
                    table.ForeignKey(
                        name: "fk_contratos_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recebiveis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: true),
                    competencia_ano = table.Column<int>(type: "integer", nullable: false),
                    competencia_mes = table.Column<int>(type: "integer", nullable: false),
                    descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    valor = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    vencimento = table.Column<DateOnly>(type: "date", nullable: false),
                    situacao = table.Column<int>(type: "integer", nullable: false),
                    valor_pago = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    pago_em = table.Column<DateOnly>(type: "date", nullable: true),
                    origem_da_baixa = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recebiveis", x => x.id);
                    table.ForeignKey(
                        name: "fk_recebiveis_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recebiveis_contratos_contrato_id",
                        column: x => x.contrato_id,
                        principalTable: "contratos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_clientes_pessoa_id",
                table: "clientes",
                column: "pessoa_id");

            migrationBuilder.CreateIndex(
                name: "ix_clientes_tenant_id_codigo",
                table: "clientes",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_clientes_tenant_id_pessoa_id",
                table: "clientes",
                columns: new[] { "tenant_id", "pessoa_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contratos_cliente_id",
                table: "contratos",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_tenant_id_cliente_id",
                table: "contratos",
                columns: new[] { "tenant_id", "cliente_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contratos_tenant_id_codigo",
                table: "contratos",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recebiveis_cliente_id",
                table: "recebiveis",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_recebiveis_contrato_id",
                table: "recebiveis",
                column: "contrato_id");

            migrationBuilder.CreateIndex(
                name: "ix_recebiveis_tenant_id_contrato_id_competencia_ano_competenci",
                table: "recebiveis",
                columns: new[] { "tenant_id", "contrato_id", "competencia_ano", "competencia_mes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recebiveis_tenant_id_situacao_vencimento",
                table: "recebiveis",
                columns: new[] { "tenant_id", "situacao", "vencimento" });

            /* Toda tabela com tenant_id nasce isolada. Ver IsolamentoPorTenant. */
            migrationBuilder.AplicarIsolamentoPorTenant("clientes");
            migrationBuilder.AplicarIsolamentoPorTenant("contratos");
            migrationBuilder.AplicarIsolamentoPorTenant("recebiveis");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RemoverIsolamentoPorTenant("recebiveis");
            migrationBuilder.RemoverIsolamentoPorTenant("contratos");
            migrationBuilder.RemoverIsolamentoPorTenant("clientes");

            migrationBuilder.DropTable(
                name: "recebiveis");

            migrationBuilder.DropTable(
                name: "contratos");

            migrationBuilder.DropTable(
                name: "clientes");
        }
    }
}
