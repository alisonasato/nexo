using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Api.Dados.Migracoes
{
    /// <summary>
    /// Funde <c>clientes</c> em <c>pessoas</c>, e o que era vínculo vira papel.
    ///
    /// <para>
    /// A tabela <c>clientes</c> apontava para <c>pessoas</c> e carregava código,
    /// regime tributário e responsável. <b>Nenhum dos três era do vínculo:</b> o
    /// regime é o da empresa na Receita, cliente ou não; o responsável é quem no
    /// escritório cuida dela; o código é o número dela. Campo que continua
    /// verdadeiro depois de o papel acabar é da pessoa, e os três desceram.
    /// </para>
    /// <para>
    /// O que sobrou do relacionamento cabe numa linha que existe ou não existe,
    /// e é isso que <c>pessoa_papeis</c> é. Com ela, a mesma pessoa acumula
    /// Cliente e Fornecedor sem virar dois cadastros com o mesmo CNPJ.
    /// </para>
    /// <para>
    /// <b>A ordem aqui é o que impede perder dado.</b> O andaime do EF apagava a
    /// tabela antes de copiar, e renomeava <c>cliente_id</c> para
    /// <c>pessoa_id</c> deixando dentro o id do cliente — que não é o id da
    /// pessoa. Contrato e recebível apontariam para o nada.
    /// </para>
    /// </summary>
    public partial class PessoaComPapeis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /* 1. O destino do dado, antes de qualquer cópia. */
            migrationBuilder.AddColumn<string>(
                name: "codigo",
                table: "pessoas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "regime_tributario",
                table: "pessoas",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<string>(
                name: "responsavel",
                table: "pessoas",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "pessoa_papeis",
                columns: table => new
                {
                    pessoa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    papel = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pessoa_papeis", x => new { x.pessoa_id, x.papel });
                    table.ForeignKey(
                        name: "fk_pessoa_papeis_pessoas_pessoa_id",
                        column: x => x.pessoa_id,
                        principalTable: "pessoas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            /* Tabela nova nasce com a política, ou nasce com um buraco. */
            migrationBuilder.AplicarIsolamentoPorTenant("pessoa_papeis");

            /*
             * 2. As chaves estrangeiras saem antes da cópia.
             *
             * O passo seguinte grava id de pessoa numa coluna que ainda aponta
             * para `clientes`. Com a chave no lugar, o banco recusaria — com
             * razão.
             */
            migrationBuilder.DropForeignKey(name: "fk_contratos_clientes_cliente_id", table: "contratos");
            migrationBuilder.DropForeignKey(name: "fk_recebiveis_clientes_cliente_id", table: "recebiveis");

            /*
             * 3. A cópia, tenant por tenant.
             *
             * `set_config` com `true` vale só nesta transação. Sem declarar o
             * tenant, a política de RLS esconde tudo e cada UPDATE atualiza zero
             * linhas sem reclamar — foi o que aconteceu na migração anterior, e
             * a conferência logo abaixo existe por causa disso.
             */
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    escritorio record;
                    orfaos     int;
                BEGIN
                    FOR escritorio IN SELECT id FROM tenants LOOP
                        PERFORM set_config('app.tenant_id', escritorio.id::text, true);

                        IF coalesce(current_setting('app.tenant_id', true), '') <> escritorio.id::text THEN
                            RAISE EXCEPTION
                                'O tenant % não foi declarado. Sob RLS, esta migração copiaria '
                                'zero linhas e terminaria sem erro.', escritorio.id;
                        END IF;

                        /* Os três campos que eram do cliente e são da pessoa. */
                        UPDATE pessoas p
                           SET codigo            = c.codigo,
                               regime_tributario = c.regime_tributario,
                               responsavel       = c.responsavel,
                               observacoes       = CASE
                                   WHEN coalesce(p.observacoes, '') = '' THEN coalesce(c.observacoes, '')
                                   WHEN coalesce(c.observacoes, '') = '' THEN p.observacoes
                                   ELSE p.observacoes || E'\n' || c.observacoes
                               END
                          FROM clientes c
                         WHERE c.pessoa_id = p.id;

                        /*
                         * Quem nunca foi cliente também precisa de código.
                         *
                         * O código passou a ser da pessoa, e o índice é único
                         * por escritório: deixar as outras com string vazia faz
                         * a criação do índice falhar na segunda delas. A
                         * numeração continua de onde os clientes pararam, e a
                         * ordem é a de cadastro — quem entrou antes tem número
                         * menor, que é o que alguém esperaria.
                         */
                        WITH numeradas AS (
                            SELECT id,
                                   row_number() OVER (ORDER BY criado_em, id)
                                   + coalesce((SELECT max(codigo::int) FROM pessoas
                                                WHERE codigo ~ '^\d+$'), 0) AS numero
                              FROM pessoas
                             WHERE codigo = ''
                        )
                        UPDATE pessoas p
                           SET codigo = numeradas.numero::text
                          FROM numeradas
                         WHERE numeradas.id = p.id;

                        /*
                         * Só cliente ativo vira papel. Cliente inativo era
                         * "deixou de ser cliente", e agora isso se diz não tendo
                         * o rótulo. A pessoa continua no cadastro, e o histórico
                         * dela continua apontando para ela.
                         */
                        INSERT INTO pessoa_papeis (pessoa_id, papel, tenant_id, criado_em)
                        SELECT c.pessoa_id, 1, c.tenant_id, c.criado_em
                          FROM clientes c
                         WHERE c.ativo;

                        /* O id do cliente vira o id da pessoa correspondente. */
                        UPDATE contratos ct
                           SET cliente_id = c.pessoa_id
                          FROM clientes c
                         WHERE c.id = ct.cliente_id;

                        UPDATE recebiveis r
                           SET cliente_id = c.pessoa_id
                          FROM clientes c
                         WHERE c.id = r.cliente_id;

                        /*
                         * Nenhum contrato ou recebível pode ter sobrado
                         * apontando para um id que não é de pessoa. Se sobrou, a
                         * migração para aqui: apagar `clientes` depois disso
                         * deixaria o histórico órfão e sem volta.
                         */
                        SELECT count(*) INTO orfaos
                          FROM (
                              SELECT cliente_id FROM contratos
                              UNION ALL
                              SELECT cliente_id FROM recebiveis
                          ) referencias
                         WHERE NOT EXISTS (
                              SELECT 1 FROM pessoas p WHERE p.id = referencias.cliente_id);

                        IF orfaos > 0 THEN
                            RAISE EXCEPTION
                                '% referência(s) do tenant % não acharam a pessoa correspondente.',
                                orfaos, escritorio.id;
                        END IF;
                    END LOOP;
                END
                $$;
                """);

            /* 4. Só agora a tabela pode sumir, e as colunas mudar de nome. */
            migrationBuilder.DropTable(name: "clientes");

            migrationBuilder.RenameColumn(name: "cliente_id", table: "recebiveis", newName: "pessoa_id");
            migrationBuilder.RenameIndex(
                name: "ix_recebiveis_cliente_id", table: "recebiveis", newName: "ix_recebiveis_pessoa_id");

            migrationBuilder.RenameColumn(name: "cliente_id", table: "contratos", newName: "pessoa_id");
            migrationBuilder.RenameIndex(
                name: "ix_contratos_tenant_id_cliente_id", table: "contratos", newName: "ix_contratos_tenant_id_pessoa_id");
            migrationBuilder.RenameIndex(
                name: "ix_contratos_cliente_id", table: "contratos", newName: "ix_contratos_pessoa_id");

            migrationBuilder.CreateIndex(
                name: "ix_pessoas_tenant_id_codigo",
                table: "pessoas",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pessoa_papeis_tenant_id_papel",
                table: "pessoa_papeis",
                columns: new[] { "tenant_id", "papel" });

            migrationBuilder.AddForeignKey(
                name: "fk_contratos_pessoas_pessoa_id",
                table: "contratos",
                column: "pessoa_id",
                principalTable: "pessoas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_recebiveis_pessoas_pessoa_id",
                table: "recebiveis",
                column: "pessoa_id",
                principalTable: "pessoas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            /*
             * A volta recria a tabela e o vínculo a partir dos papéis. Não fica
             * idêntica ao que havia: quem deixou de ser cliente perdeu o rótulo,
             * então volta como cliente inativo só se ainda tiver contrato ou
             * recebível — e as observações que foram fundidas não se separam de
             * novo. Desfazer migração com fusão de dado sempre aproxima.
             */
            migrationBuilder.DropForeignKey(name: "fk_contratos_pessoas_pessoa_id", table: "contratos");
            migrationBuilder.DropForeignKey(name: "fk_recebiveis_pessoas_pessoa_id", table: "recebiveis");

            migrationBuilder.CreateTable(
                name: "clientes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pessoa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    observacoes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    regime_tributario = table.Column<int>(type: "integer", nullable: false),
                    responsavel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
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

            migrationBuilder.AplicarIsolamentoPorTenant("clientes");

            migrationBuilder.Sql(
                """
                DO $$
                DECLARE escritorio record;
                BEGIN
                    FOR escritorio IN SELECT id FROM tenants LOOP
                        PERFORM set_config('app.tenant_id', escritorio.id::text, true);

                        INSERT INTO clientes (id, pessoa_id, tenant_id, ativo, codigo,
                                              criado_em, observacoes, regime_tributario, responsavel)
                        SELECT gen_random_uuid(), p.id, p.tenant_id,
                               EXISTS (SELECT 1 FROM pessoa_papeis pp
                                        WHERE pp.pessoa_id = p.id AND pp.papel = 1),
                               p.codigo, p.criado_em, p.observacoes, p.regime_tributario, p.responsavel
                          FROM pessoas p
                         WHERE EXISTS (SELECT 1 FROM pessoa_papeis pp
                                        WHERE pp.pessoa_id = p.id AND pp.papel = 1)
                            OR EXISTS (SELECT 1 FROM contratos ct WHERE ct.pessoa_id = p.id)
                            OR EXISTS (SELECT 1 FROM recebiveis r WHERE r.pessoa_id = p.id);

                        UPDATE contratos ct SET pessoa_id = c.id
                          FROM clientes c WHERE c.pessoa_id = ct.pessoa_id;

                        UPDATE recebiveis r SET pessoa_id = c.id
                          FROM clientes c WHERE c.pessoa_id = r.pessoa_id;
                    END LOOP;
                END
                $$;
                """);

            migrationBuilder.DropTable(name: "pessoa_papeis");

            migrationBuilder.DropIndex(name: "ix_pessoas_tenant_id_codigo", table: "pessoas");
            migrationBuilder.DropColumn(name: "codigo", table: "pessoas");
            migrationBuilder.DropColumn(name: "regime_tributario", table: "pessoas");
            migrationBuilder.DropColumn(name: "responsavel", table: "pessoas");

            migrationBuilder.RenameColumn(name: "pessoa_id", table: "recebiveis", newName: "cliente_id");
            migrationBuilder.RenameIndex(
                name: "ix_recebiveis_pessoa_id", table: "recebiveis", newName: "ix_recebiveis_cliente_id");

            migrationBuilder.RenameColumn(name: "pessoa_id", table: "contratos", newName: "cliente_id");
            migrationBuilder.RenameIndex(
                name: "ix_contratos_tenant_id_pessoa_id", table: "contratos", newName: "ix_contratos_tenant_id_cliente_id");
            migrationBuilder.RenameIndex(
                name: "ix_contratos_pessoa_id", table: "contratos", newName: "ix_contratos_cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_clientes_tenant_id_codigo", table: "clientes",
                columns: new[] { "tenant_id", "codigo" }, unique: true);
            migrationBuilder.CreateIndex(
                name: "ix_clientes_tenant_id_pessoa_id", table: "clientes",
                columns: new[] { "tenant_id", "pessoa_id" }, unique: true);
            migrationBuilder.CreateIndex(
                name: "ix_clientes_pessoa_id", table: "clientes", column: "pessoa_id");

            migrationBuilder.AddForeignKey(
                name: "fk_contratos_clientes_cliente_id", table: "contratos", column: "cliente_id",
                principalTable: "clientes", principalColumn: "id", onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_recebiveis_clientes_cliente_id", table: "recebiveis", column: "cliente_id",
                principalTable: "clientes", principalColumn: "id", onDelete: ReferentialAction.Restrict);
        }
    }
}
