using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dominio;
using Npgsql;

namespace Nexo.Api.Testes;

/// <summary>
/// O teste obrigatório da decisão Q30: <b>um tenant não lê o outro</b>.
///
/// Ele existe desde o primeiro dia porque a RLS falha em silêncio. Quando a
/// política está errada, nada estoura: a consulta simplesmente devolve linhas
/// — inclusive as de outro cliente. Conferência manual não pega isso, porque
/// em desenvolvimento existe um tenant só.
///
/// Cada teste cria os próprios tenants com identificadores novos, então eles
/// não se atrapalham e o banco não precisa ser limpo entre execuções.
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class IsolamentoEntreTenants(BancoDeTestes banco)
{
    [Fact]
    public async Task Cada_tenant_enxerga_apenas_as_proprias_empresas()
    {
        var (tenantA, tenantB) = await CriarDoisTenantsComUmaEmpresaCada();

        await using (var contexto = banco.Criar(tenantA))
        {
            var visiveis = await contexto.Empresas.ToListAsync();
            Assert.All(visiveis, empresa => Assert.Equal(tenantA, empresa.TenantId));
            Assert.Contains(visiveis, empresa => empresa.RazaoSocial == $"Empresa de {tenantA}");
        }

        await using (var contexto = banco.Criar(tenantB))
        {
            var visiveis = await contexto.Empresas.ToListAsync();
            Assert.All(visiveis, empresa => Assert.Equal(tenantB, empresa.TenantId));
            Assert.DoesNotContain(visiveis, empresa => empresa.TenantId == tenantA);
        }
    }

    [Fact]
    public async Task Sem_tenant_nao_se_enxerga_nada()
    {
        await CriarDoisTenantsComUmaEmpresaCada();

        await using var contexto = banco.Criar(tenant: null);

        // A falha é fechada: ausência de tenant não é "todos", é "nenhum".
        Assert.Empty(await contexto.Empresas.ToListAsync());
    }

    [Fact]
    public async Task Nao_se_grava_linha_carimbada_com_o_tenant_de_outro()
    {
        var (tenantA, tenantB) = await CriarDoisTenantsComUmaEmpresaCada();

        await using var contexto = banco.Criar(tenantA);
        contexto.Empresas.Add(new Empresa
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB,
            RazaoSocial = "Tentativa de plantar linha no vizinho",
            Cnpj = Cnpj(),
        });

        // É o WITH CHECK da política recusando: ler o alheio é um problema,
        // escrever no alheio é outro, e os dois precisam estar cobertos.
        var erro = await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync());
        var causa = Assert.IsType<PostgresException>(erro.InnerException);
        Assert.Equal("42501", causa.SqlState);
    }

    [Fact]
    public async Task Conexao_reaproveitada_do_pool_nao_carrega_o_tenant_anterior()
    {
        var (tenantA, tenantB) = await CriarDoisTenantsComUmaEmpresaCada();

        /*
         * Este é o teste da armadilha número 3 da decisão Q22. Abrir como A,
         * fechar, e abrir como B em seguida devolve — quase sempre — a mesma
         * conexão física do pool. Se o tenant não for regravado em toda
         * abertura, B enxerga o que é de A, e não existe erro nenhum para
         * denunciar isso.
         */
        for (var rodada = 0; rodada < 5; rodada++)
        {
            await using (var contexto = banco.Criar(tenantA))
            {
                Assert.All(await contexto.Empresas.ToListAsync(),
                    empresa => Assert.Equal(tenantA, empresa.TenantId));
            }

            await using (var contexto = banco.Criar(tenantB))
            {
                Assert.All(await contexto.Empresas.ToListAsync(),
                    empresa => Assert.Equal(tenantB, empresa.TenantId));
            }
        }
    }

    /* ------------------------------------------------------------ apoio */

    private async Task<(Guid TenantA, Guid TenantB)> CriarDoisTenantsComUmaEmpresaCada()
    {
        var tenantA = await CriarTenantComEmpresa();
        var tenantB = await CriarTenantComEmpresa();
        return (tenantA, tenantB);
    }

    private async Task<Guid> CriarTenantComEmpresa()
    {
        var id = Guid.NewGuid();

        // A tabela de tenants é o registro e não tem RLS: é o que a
        // autenticação consulta antes de saber qual é o tenant.
        await using (var contexto = banco.Criar(tenant: null))
        {
            contexto.Tenants.Add(new Tenant { Id = id, Nome = $"Escritório {id:N}"[..30] });
            await contexto.SaveChangesAsync();
        }

        // Já a empresa só entra falando como o próprio tenant — o WITH CHECK
        // não deixaria de outro jeito.
        await using (var contexto = banco.Criar(id))
        {
            contexto.Empresas.Add(new Empresa
            {
                Id = Guid.NewGuid(),
                TenantId = id,
                RazaoSocial = $"Empresa de {id}",
                Cnpj = Cnpj(),
            });
            await contexto.SaveChangesAsync();
        }

        return id;
    }

    private static string Cnpj() => Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
}
