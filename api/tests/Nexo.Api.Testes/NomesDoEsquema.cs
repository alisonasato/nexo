using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace Nexo.Api.Testes;

/// <summary>
/// O banco tem os nomes que o modelo acha que ele tem.
///
/// <para>
/// <b>Existe por causa das migrações escritas à mão.</b> Renomear uma tabela
/// leva junto chave primária, chaves estrangeiras, índices e a política de
/// isolamento, e o EF não sabe fazer isso: ele apaga a tabela e cria outra, com
/// os dados dentro. A renomeação é escrita à mão, e esquecer um nome ali não
/// quebra nada no dia. Quebra meses depois, quando uma migração gerada tenta
/// apagar ou renomear um índice pelo nome que o modelo conhece e o banco não
/// tem, e a falha aparece na subida de produção, longe da causa.
/// </para>
/// <para>
/// A comparação vai num sentido só: todo nome do modelo existe no banco. O
/// banco tem nomes que o modelo não conhece, como a chave do histórico de
/// migrações, e isso não é defeito.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class NomesDoEsquema(BancoDeTestes banco)
{
    [Fact]
    public async Task Chaves_e_indices_do_modelo_existem_com_o_mesmo_nome_no_banco()
    {
        await using var contexto = banco.Criar(tenant: null);
        var modelo = contexto.GetService<IDesignTimeModel>().Model;

        var esperados = new SortedSet<string>(StringComparer.Ordinal);

        /* Tipos próprios, como o endereço, moram na tabela do dono: não têm chave nem índice seus no banco. */
        foreach (var entidade in modelo.GetEntityTypes().Where(e => !e.IsOwned() && e.GetTableName() is not null))
        {
            foreach (var chave in entidade.GetDeclaredKeys())
                esperados.Add(chave.GetName()!);

            foreach (var estrangeira in entidade.GetDeclaredForeignKeys().Where(f => !f.IsOwnership))
                esperados.Add(estrangeira.GetConstraintName()!);

            foreach (var indice in entidade.GetDeclaredIndexes())
                esperados.Add(indice.GetDatabaseName()!);
        }

        var existentes = await Ler(
            """
            SELECT indexname FROM pg_indexes WHERE schemaname = 'public'
            UNION
            SELECT c.conname FROM pg_constraint c
                JOIN pg_namespace n ON n.oid = c.connamespace
                WHERE n.nspname = 'public'
            """);

        Assert.Empty(esperados.Except(existentes));
    }

    /// <summary>
    /// <c>RemoverIsolamentoPorTenant</c> apaga a política pelo nome
    /// <c>{tabela}_do_tenant</c>, com <c>IF EXISTS</c>. Uma tabela renomeada
    /// que guardasse a política com o nome antigo faria essa remoção passar sem
    /// remover nada, e sem erro nenhum para contar.
    /// </summary>
    [Fact]
    public async Task Toda_politica_de_isolamento_leva_o_nome_da_propria_tabela()
    {
        var politicas = await Ler(
            "SELECT tablename || ' ' || policyname FROM pg_policies WHERE schemaname = 'public'");

        Assert.NotEmpty(politicas);
        Assert.All(politicas, linha =>
        {
            var partes = linha.Split(' ');
            Assert.Equal($"{partes[0]}_do_tenant", partes[1]);
        });
    }

    /* ------------------------------------------------------------ apoio */

    private async Task<List<string>> Ler(string sql)
    {
        await using var conexao = new NpgsqlConnection(banco.Conexao);
        await conexao.OpenAsync();

        await using var comando = new NpgsqlCommand(sql, conexao);
        await using var leitor = await comando.ExecuteReaderAsync();

        var linhas = new List<string>();
        while (await leitor.ReadAsync())
            linhas.Add(leitor.GetString(0));

        return linhas;
    }
}
