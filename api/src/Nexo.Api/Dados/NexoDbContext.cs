using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dominio;

namespace Nexo.Api.Dados;

public class NexoDbContext(DbContextOptions<NexoDbContext> opcoes)
    : IdentityDbContext<Usuario, IdentityRole<Guid>, Guid>(opcoes)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Pessoa> Pessoas => Set<Pessoa>();
    public DbSet<PessoaPapel> PessoaPapeis => Set<PessoaPapel>();
    public DbSet<Contrato> Contratos => Set<Contrato>();
    public DbSet<Recebivel> Recebiveis => Set<Recebivel>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        base.OnModelCreating(modelo);

        modelo.Entity<Tenant>(tenant =>
        {
            tenant.HasKey(t => t.Id);
            tenant.Property(t => t.Nome).HasMaxLength(200);
            tenant.Property(t => t.CriadoEm).HasDefaultValueSql("now()");
        });

        modelo.Entity<Empresa>(empresa =>
        {
            empresa.HasKey(e => e.Id);
            empresa.Property(e => e.RazaoSocial).HasMaxLength(200);
            empresa.Property(e => e.NomeFantasia).HasMaxLength(200);
            empresa.Property(e => e.Cnpj).HasMaxLength(14);
            empresa.Property(e => e.CriadoEm).HasDefaultValueSql("now()");

            /*
             * O CNPJ é único dentro do tenant, não no banco inteiro: dois
             * escritórios diferentes podem ter o mesmo cliente, e nenhum dos
             * dois pode saber disso.
             */
            empresa.HasIndex(e => new { e.TenantId, e.Cnpj }).IsUnique();

            empresa.HasOne(e => e.Tenant)
                .WithMany(t => t.Empresas)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            empresa.HasOne(e => e.Matriz)
                .WithMany(m => m.Filiais)
                .HasForeignKey(e => e.MatrizId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelo.Entity<Pessoa>(pessoa =>
        {
            pessoa.HasKey(p => p.Id);
            pessoa.Property(p => p.Nome).HasMaxLength(200);
            pessoa.Property(p => p.NomeFantasia).HasMaxLength(200);
            pessoa.Property(p => p.Documento).HasMaxLength(14);
            pessoa.Property(p => p.InscricaoEstadual).HasMaxLength(30);
            pessoa.Property(p => p.InscricaoMunicipal).HasMaxLength(30);
            pessoa.Property(p => p.Email).HasMaxLength(200);
            pessoa.Property(p => p.Telefone).HasMaxLength(20);
            pessoa.Property(p => p.Celular).HasMaxLength(20);
            pessoa.Property(p => p.Observacoes).HasMaxLength(4000);
            pessoa.Property(p => p.CriadoEm).HasDefaultValueSql("now()");
            pessoa.Property(p => p.AtualizadoEm).HasDefaultValueSql("now()");

            /* Endereço vira colunas na mesma tabela: é parte da pessoa, não outra entidade. */
            pessoa.OwnsOne(p => p.Endereco, endereco =>
            {
                endereco.Property(e => e.Cep).HasMaxLength(8);
                endereco.Property(e => e.Logradouro).HasMaxLength(200);
                endereco.Property(e => e.Numero).HasMaxLength(20);
                endereco.Property(e => e.Complemento).HasMaxLength(100);
                endereco.Property(e => e.Bairro).HasMaxLength(100);
                endereco.Property(e => e.Cidade).HasMaxLength(100);
                endereco.Property(e => e.Uf).HasMaxLength(2);
            });

            /*
             * Documento único dentro do tenant. O índice é a última defesa: a
             * validação avisa com uma mensagem decente, e o banco garante que
             * duas gravações simultâneas não furem a checagem.
             */
            pessoa.HasIndex(p => new { p.TenantId, p.Documento }).IsUnique();

            /* A busca da listagem ordena por nome; o índice evita varrer tudo. */
            pessoa.HasIndex(p => new { p.TenantId, p.Nome });

            pessoa.Property(p => p.Codigo).HasMaxLength(20);
            pessoa.Property(p => p.Responsavel).HasMaxLength(200);
            pessoa.HasIndex(p => new { p.TenantId, p.Codigo }).IsUnique();
        });

        modelo.Entity<PessoaPapel>(papel =>
        {
            /*
             * A chave é o par pessoa e papel: a linha existir É o fato. Não há
             * id próprio porque não há nada a identificar além dela mesma, e
             * não há como a mesma pessoa ter o mesmo papel duas vezes.
             */
            papel.HasKey(p => new { p.PessoaId, p.Papel });
            papel.Property(p => p.CriadoEm).HasDefaultValueSql("now()");

            /* Listar quem é cliente é a consulta mais comum desta tabela. */
            papel.HasIndex(p => new { p.TenantId, p.Papel });

            /*
             * Cascata aqui, e não Restrict: o rótulo não tem vida própria. Se a
             * pessoa fosse apagada, guardar o papel dela seria guardar a sombra
             * de um cadastro que não existe. Quem impede a pessoa de sumir é o
             * Restrict de contrato e recebível, que apontam para ela.
             */
            papel.HasOne(p => p.Pessoa)
                .WithMany(p => p.Papeis)
                .HasForeignKey(p => p.PessoaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelo.Entity<Contrato>(contrato =>
        {
            contrato.HasKey(c => c.Id);
            contrato.Property(c => c.Codigo).HasMaxLength(20);
            contrato.Property(c => c.Descricao).HasMaxLength(200);
            contrato.Property(c => c.Observacoes).HasMaxLength(4000);
            contrato.Property(c => c.CriadoEm).HasDefaultValueSql("now()");
            contrato.Property(c => c.AtualizadoEm).HasDefaultValueSql("now()");

            /*
             * Dinheiro em decimal com escala fixa, nunca em ponto flutuante.
             * 14 dígitos e 2 casas: valor de honorário não passa disso, e o
             * banco recusa em vez de arredondar em silêncio.
             */
            contrato.Property(c => c.Valor).HasPrecision(14, 2);

            contrato.HasIndex(c => new { c.TenantId, c.Codigo }).IsUnique();
            contrato.HasIndex(c => new { c.TenantId, c.PessoaId });

            contrato.HasOne(c => c.Pessoa)
                .WithMany()
                .HasForeignKey(c => c.PessoaId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelo.Entity<Recebivel>(recebivel =>
        {
            recebivel.HasKey(r => r.Id);
            recebivel.Property(r => r.Descricao).HasMaxLength(200);
            recebivel.Property(r => r.OrigemDaBaixa).HasMaxLength(30);
            recebivel.Property(r => r.MotivoDoCancelamento).HasMaxLength(200);
            recebivel.Property(r => r.Valor).HasPrecision(14, 2);
            recebivel.Property(r => r.ValorPago).HasPrecision(14, 2);
            recebivel.Property(r => r.CriadoEm).HasDefaultValueSql("now()");
            recebivel.Property(r => r.AtualizadoEm).HasDefaultValueSql("now()");

            /*
             * A trava que impede cobrar duas vezes a mesma competência.
             *
             * Não é uma checagem no código: é o banco recusando. Vale inclusive
             * quando dois cliques acontecem ao mesmo tempo, que é justamente
             * quando uma checagem no código falha. Avulsos têm ContratoId nulo,
             * e no Postgres nulos não colidem entre si — então eles não são
             * afetados por esta regra.
             */
            recebivel.HasIndex(r => new { r.TenantId, r.ContratoId, r.CompetenciaAno, r.CompetenciaMes })
                .IsUnique()
                /*
                 * Índice parcial: cancelados ficam de fora. É o que permite
                 * corrigir uma geração errada — cancela e gera de novo — sem
                 * apagar o histórico do que foi cancelado e por quê.
                 * 3 é SituacaoRecebivel.Cancelado.
                 */
                .HasFilter("situacao <> 3");

            recebivel.HasIndex(r => new { r.TenantId, r.Situacao, r.Vencimento });

            recebivel.HasOne(r => r.Pessoa)
                .WithMany()
                .HasForeignKey(r => r.PessoaId)
                .OnDelete(DeleteBehavior.Restrict);

            recebivel.HasOne(r => r.Contrato)
                .WithMany()
                .HasForeignKey(r => r.ContratoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelo.Entity<Usuario>(usuario =>
        {
            usuario.Property(u => u.Nome).HasMaxLength(200);

            usuario.HasOne(u => u.Tenant)
                .WithMany()
                .HasForeignKey(u => u.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            usuario.HasIndex(u => u.TenantId);
        });
    }
}
