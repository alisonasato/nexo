using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Api.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class NaturezaDoLancamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_lancamentos_tenant_id_situacao_vencimento",
                table: "lancamentos");

            migrationBuilder.AddColumn<int>(
                name: "natureza",
                table: "lancamentos",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            /* As linhas que já existem são todas a receber, a única natureza até aqui. Depois o padrão sai: nenhuma gravação futura herda natureza sem dizer qual. */
            migrationBuilder.Sql("ALTER TABLE lancamentos ALTER COLUMN natureza DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_tenant_id_natureza_situacao_vencimento",
                table: "lancamentos",
                columns: new[] { "tenant_id", "natureza", "situacao", "vencimento" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_lancamentos_tenant_id_natureza_situacao_vencimento",
                table: "lancamentos");

            migrationBuilder.DropColumn(
                name: "natureza",
                table: "lancamentos");

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_tenant_id_situacao_vencimento",
                table: "lancamentos",
                columns: new[] { "tenant_id", "situacao", "vencimento" });
        }
    }
}
