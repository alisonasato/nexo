using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Api.Dados.Migracoes
{
    /// <summary>
    /// Tira o <c>C-</c> e os zeros à esquerda do código do cliente: <c>C-0001</c>
    /// vira <c>1</c>.
    ///
    /// <para>
    /// É decisão de quem usa: o código do cliente é o que se fala ao telefone, e
    /// número puro é mais curto de dizer. O código do contrato segue com prefixo
    /// e preenchimento (<c>C0001</c>), de propósito — ele aparece em documento,
    /// não em conversa.
    /// </para>
    /// <para>
    /// <b>Os códigos existentes são convertidos, não deixados para trás.</b>
    /// Metade da carteira em <c>C-0001</c> e metade em <c>7</c> seria pior do que
    /// qualquer um dos dois formatos sozinho. O número é preservado: quem era
    /// <c>C-0003</c> passa a ser <c>3</c>, então nenhuma conversa antiga fica
    /// errada.
    /// </para>
    /// <para>
    /// <b>Migração de dado em tabela com RLS precisa dizer de quem está falando.</b>
    /// A primeira versão disto era um <c>UPDATE</c> direto, e ele atualizou zero
    /// linhas sem reclamar: a migração roda com a conexão da aplicação, sem
    /// <c>app.tenant_id</c>, e a política esconde tudo. Não houve erro, não
    /// houve aviso, e os códigos ficaram como estavam. Daí o laço por tenant.
    /// </para>
    /// <para>
    /// A primeira trava contra isso também não serviu: contar as linhas que
    /// sobraram obedece à mesma política que as escondeu, então ela via zero e
    /// aprovava. O que funciona é conferir que o tenant <b>foi declarado</b>,
    /// antes de escrever — e essa conferência foi testada tirando o
    /// <c>set_config</c> de propósito, com a migração caindo.
    /// </para>
    /// </summary>
    public partial class CodigoDoClienteSemPrefixo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*
             * `set_config` com `true` vale só dentro desta transação: a migração
             * declara em nome de quem escreve, tenant por tenant, e nada
             * sobra ligado depois do commit.
             *
             * Só converte o que tem a forma antiga, e a expressão exige a linha
             * inteira: `^C-0*(\d+)$`. Código digitado fora do padrão fica
             * intacto — adivinhar o que ele queria dizer é como se estraga dado
             * numa migração.
             *
             * A unicidade se mantém sozinha: o índice é por (tenant_id, codigo),
             * os números já eram únicos dentro do escritório, e tirar prefixo e
             * zeros de números distintos devolve números distintos.
             */
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    escritorio record;
                    faltando   int;
                BEGIN
                    FOR escritorio IN SELECT id FROM tenants LOOP
                        PERFORM set_config('app.tenant_id', escritorio.id::text, true);

                        /*
                         * Confere que o tenant foi declarado ANTES de escrever.
                         *
                         * É a única trava que funciona aqui, e o caminho até
                         * ela vale registrar: a primeira tentativa contava as
                         * linhas que sobraram sem converter. Não adiantou nada,
                         * porque a contagem obedece à mesma política que
                         * escondeu as linhas do UPDATE — ela via zero e dava
                         * por bom. Conferência cega não confere.
                         */
                        IF coalesce(current_setting('app.tenant_id', true), '') <> escritorio.id::text THEN
                            RAISE EXCEPTION
                                'O tenant % não foi declarado na sessão. Sob RLS, este UPDATE '
                                'não veria linha nenhuma e terminaria sem erro.', escritorio.id;
                        END IF;

                        UPDATE clientes
                           SET codigo = regexp_replace(codigo, '^C-0*(\d+)$', '\1')
                         WHERE codigo ~ '^C-0*\d+$';

                        /*
                         * Esta segunda conferência tem alcance menor, e é
                         * honesto dizer qual: com o tenant declarado, ela pega
                         * código que a expressão não soube converter. Não pega
                         * o caso da RLS, que é papel da trava acima.
                         */
                        SELECT count(*) INTO faltando
                          FROM clientes
                         WHERE codigo ~ '^C-0*\d+$';

                        IF faltando > 0 THEN
                            RAISE EXCEPTION
                                'Sobraram % cliente(s) do tenant % no formato antigo.',
                                faltando, escritorio.id;
                        END IF;
                    END LOOP;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            /*
             * A volta remonta a forma antiga a partir do número. Não fica
             * idêntica se alguém já tiver criado código com mais de quatro
             * dígitos — 12345 volta como C-12345, sem preenchimento —, e isso é
             * melhor do que truncar.
             */
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE escritorio record;
                BEGIN
                    FOR escritorio IN SELECT id FROM tenants LOOP
                        PERFORM set_config('app.tenant_id', escritorio.id::text, true);

                        UPDATE clientes
                           SET codigo = 'C-' || lpad(codigo, 4, '0')
                         WHERE codigo ~ '^\d+$';
                    END LOOP;
                END
                $$;
                """);
        }
    }
}
