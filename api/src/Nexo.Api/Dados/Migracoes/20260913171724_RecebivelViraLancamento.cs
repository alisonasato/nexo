using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Api.Dados.Migracoes
{
    /// <summary>
    /// A tabela de recebíveis vira a tabela de lançamentos, com os dados dentro.
    ///
    /// <para>
    /// <b>Escrita à mão.</b> Com classe e tabela de nome novo, o EF gerou apagar
    /// <c>recebiveis</c> e criar <c>lancamentos</c>: em produção, todo o dinheiro
    /// a receber sumiria na subida. Aqui cada nome é renomeado, e nada é
    /// recriado.
    /// </para>
    /// <para>
    /// Renomear a tabela não renomeia o que leva o nome dela. Chave primária,
    /// chaves estrangeiras, índices e a política de isolamento continuariam
    /// dizendo <c>recebiveis</c>, e a próxima migração gerada, que procura esses
    /// objetos pelo nome do modelo, falharia. O teste <c>NomesDoEsquema</c>
    /// confere que nenhum ficou para trás.
    /// </para>
    /// </summary>
    public partial class RecebivelViraLancamento : Migration
    {
        /* A chave primária e as estrangeiras, com a tabela onde moram depois da renomeação. */
        private static readonly (string Tabela, string Antigo, string Novo)[] Restricoes =
        [
            ("lancamentos", "pk_recebiveis", "pk_lancamentos"),
            ("lancamentos", "fk_recebiveis_contratos_contrato_id", "fk_lancamentos_contratos_contrato_id"),
            ("lancamentos", "fk_recebiveis_pessoas_pessoa_id", "fk_lancamentos_pessoas_pessoa_id"),
            ("lancamentos", "fk_recebiveis_recebiveis_renegociado_de_id", "fk_lancamentos_lancamentos_renegociado_de_id"),
            ("renegociacoes", "fk_renegociacoes_recebiveis_origem_id", "fk_renegociacoes_lancamentos_origem_id"),
        ];

        private static readonly (string Antigo, string Novo)[] Indices =
        [
            ("ix_recebiveis_contrato_id", "ix_lancamentos_contrato_id"),
            ("ix_recebiveis_pessoa_id", "ix_lancamentos_pessoa_id"),
            ("ix_recebiveis_renegociado_de_id", "ix_lancamentos_renegociado_de_id"),
            ("ix_recebiveis_tenant_id_parcelamento_id", "ix_lancamentos_tenant_id_parcelamento_id"),
            ("ix_recebiveis_tenant_id_situacao_vencimento", "ix_lancamentos_tenant_id_situacao_vencimento"),

            /* O índice da mensalidade única, com os dois nomes cortados como o EF os gerou. */
            ("ix_recebiveis_tenant_id_contrato_id_competencia_ano_competenci", "ix_lancamentos_tenant_id_contrato_id_competencia_ano_competenc"),
        ];

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "recebiveis", newName: "lancamentos");

            foreach (var (tabela, antigo, novo) in Restricoes)
                migrationBuilder.Sql($"ALTER TABLE {tabela} RENAME CONSTRAINT {antigo} TO {novo};");

            foreach (var (antigo, novo) in Indices)
                migrationBuilder.RenameIndex(name: antigo, table: "lancamentos", newName: novo);

            migrationBuilder.Sql("ALTER POLICY recebiveis_do_tenant ON lancamentos RENAME TO lancamentos_do_tenant;");

            migrationBuilder.RenameColumn(name: "recebivel_id", table: "eventos_de_cobranca", newName: "lancamento_id");
            migrationBuilder.RenameIndex(
                name: "ix_eventos_de_cobranca_tenant_id_recebivel_id",
                table: "eventos_de_cobranca",
                newName: "ix_eventos_de_cobranca_tenant_id_lancamento_id");
        }

        /* O caminho de volta, na ordem inversa: tudo que mora em lancamentos antes de a tabela voltar a se chamar recebiveis. */
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "ix_eventos_de_cobranca_tenant_id_lancamento_id",
                table: "eventos_de_cobranca",
                newName: "ix_eventos_de_cobranca_tenant_id_recebivel_id");
            migrationBuilder.RenameColumn(name: "lancamento_id", table: "eventos_de_cobranca", newName: "recebivel_id");

            migrationBuilder.Sql("ALTER POLICY lancamentos_do_tenant ON lancamentos RENAME TO recebiveis_do_tenant;");

            foreach (var (antigo, novo) in Indices)
                migrationBuilder.RenameIndex(name: novo, table: "lancamentos", newName: antigo);

            foreach (var (tabela, antigo, novo) in Restricoes)
                migrationBuilder.Sql($"ALTER TABLE {tabela} RENAME CONSTRAINT {novo} TO {antigo};");

            migrationBuilder.RenameTable(name: "lancamentos", newName: "recebiveis");
        }
    }
}
