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
    public DbSet<Lancamento> Lancamentos => Set<Lancamento>();
    public DbSet<EventoDeCobranca> EventosDeCobranca => Set<EventoDeCobranca>();
    public DbSet<Renegociacao> Renegociacoes => Set<Renegociacao>();
    public DbSet<ContaBancaria> ContasBancarias => Set<ContaBancaria>();
    public DbSet<MovimentoDeConta> MovimentosDeConta => Set<MovimentoDeConta>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<CentroDeCusto> CentrosDeCusto => Set<CentroDeCusto>();
    public DbSet<EventoDeAuditoria> EventosDeAuditoria => Set<EventoDeAuditoria>();
    public DbSet<Recorrencia> Recorrencias => Set<Recorrencia>();

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

            pessoa.Property(p => p.ClienteNoAsaas).HasMaxLength(60);
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
             * Restrict de contrato e lançamento, que apontam para ela.
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

        modelo.Entity<Lancamento>(lancamento =>
        {
            lancamento.HasKey(r => r.Id);
            lancamento.Property(r => r.Descricao).HasMaxLength(200);
            lancamento.Property(r => r.OrigemDaBaixa).HasMaxLength(30);
            lancamento.Property(r => r.CobrancaId).HasMaxLength(60);
            lancamento.Property(r => r.CobrancaUrl).HasMaxLength(300);

            /*
             * A situação é a trava de concorrência do lançamento.
             *
             * Toda gravação passa a exigir que a situação ainda seja a lida.
             * Sem isto, a baixa manual e o aviso do PSP podiam ler "em aberto"
             * ao mesmo tempo e gravar os dois, a segunda por cima da primeira:
             * o dinheiro não duplicava, mas a origem e o valor da baixa ficavam
             * com o que chegou por último. Com a baixa em lote, esse encontro
             * deixa de ser raro.
             */
            lancamento.Property(r => r.Situacao).IsConcurrencyToken();
            lancamento.Property(r => r.MotivoDoCancelamento).HasMaxLength(200);
            lancamento.Property(r => r.Valor).HasPrecision(14, 2);
            lancamento.Property(r => r.ValorPago).HasPrecision(14, 2);
            lancamento.Property(r => r.Desconto).HasPrecision(14, 2);
            lancamento.Property(r => r.Juros).HasPrecision(14, 2);
            lancamento.Property(r => r.Multa).HasPrecision(14, 2);
            lancamento.Property(r => r.CriadoEm).HasDefaultValueSql("now()");
            lancamento.Property(r => r.AtualizadoEm).HasDefaultValueSql("now()");

            /*
             * A trava que impede cobrar duas vezes a mesma competência.
             *
             * Não é uma checagem no código: é o banco recusando. Vale inclusive
             * quando dois cliques acontecem ao mesmo tempo, que é justamente
             * quando uma checagem no código falha. Avulsos têm ContratoId nulo,
             * e no Postgres nulos não colidem entre si — então eles não são
             * afetados por esta regra.
             */
            lancamento.HasIndex(r => new { r.TenantId, r.ContratoId, r.CompetenciaAno, r.CompetenciaMes })
                .IsUnique()
                /*
                 * Índice parcial: cancelados ficam de fora. É o que permite
                 * corrigir uma geração errada — cancela e gera de novo — sem
                 * apagar o histórico do que foi cancelado e por quê.
                 * 3 é SituacaoLancamento.Cancelado.
                 *
                 * Renegociado, que é 4, fica dentro de propósito: o título
                 * renegociado continua ocupando a competência, e é o que impede
                 * gerar a mesma mensalidade de novo depois do acordo.
                 */
                .HasFilter("situacao <> 3");

            /* A listagem sempre recorta por natureza antes de olhar situação e vencimento. */
            lancamento.HasIndex(r => new { r.TenantId, r.Natureza, r.Situacao, r.Vencimento });

            /* As parcelas de um mesmo lançamento são lidas juntas. */
            lancamento.HasIndex(r => new { r.TenantId, r.ParcelamentoId });

            /*
             * A parcela nova aponta para o título que ela substitui. Restrict,
             * porque o renegociado é o histórico do acordo: apagá-lo deixaria
             * parcelas sem origem.
             */
            lancamento.HasOne<Lancamento>()
                .WithMany()
                .HasForeignKey(r => r.RenegociadoDeId)
                .OnDelete(DeleteBehavior.Restrict);

            lancamento.HasOne(r => r.Pessoa)
                .WithMany()
                .HasForeignKey(r => r.PessoaId)
                .OnDelete(DeleteBehavior.Restrict);

            lancamento.HasOne(r => r.Contrato)
                .WithMany()
                .HasForeignKey(r => r.ContratoId)
                .OnDelete(DeleteBehavior.Restrict);

            /*
             * A mesma trava do contrato, para a recorrência: um lançamento por
             * competência, com os cancelados de fora para a correção poder gerar de
             * novo. Gerar duas vezes, ou duas pessoas ao mesmo tempo, esbarra aqui.
             */
            lancamento.HasIndex(r => new { r.TenantId, r.RecorrenciaId, r.CompetenciaAno, r.CompetenciaMes })
                .IsUnique()
                .HasFilter("situacao <> 3");

            lancamento.HasOne<Recorrencia>()
                .WithMany()
                .HasForeignKey(r => r.RecorrenciaId)
                .OnDelete(DeleteBehavior.Restrict);

            /* Restrict: categoria e centro não se apagam, e o lançamento não fica apontando para o nada. */
            lancamento.HasOne(r => r.Categoria)
                .WithMany()
                .HasForeignKey(r => r.CategoriaId)
                .OnDelete(DeleteBehavior.Restrict);

            lancamento.HasOne(r => r.CentroDeCusto)
                .WithMany()
                .HasForeignKey(r => r.CentroDeCustoId)
                .OnDelete(DeleteBehavior.Restrict);

            /* O filtro da tela lê por categoria e por centro, dentro do escritório. */
            lancamento.HasIndex(r => new { r.TenantId, r.CategoriaId });
            lancamento.HasIndex(r => new { r.TenantId, r.CentroDeCustoId });
        });

        modelo.Entity<Renegociacao>(renegociacao =>
        {
            renegociacao.HasKey(r => r.Id);
            renegociacao.Property(r => r.Motivo).HasMaxLength(200);
            renegociacao.Property(r => r.Juros).HasPrecision(14, 2);
            renegociacao.Property(r => r.Multa).HasPrecision(14, 2);
            renegociacao.Property(r => r.Desconto).HasPrecision(14, 2);
            renegociacao.Property(r => r.CriadoEm).HasDefaultValueSql("now()");

            /*
             * Um título se renegocia uma vez: depois disso ele não está mais em
             * aberto. O índice é a segunda defesa, atrás da trava de
             * concorrência da situação, para o caso de dois cliques chegarem
             * juntos.
             */
            renegociacao.HasIndex(r => r.OrigemId).IsUnique();
            renegociacao.HasIndex(r => r.TenantId);

            renegociacao.HasOne(r => r.Origem)
                .WithMany()
                .HasForeignKey(r => r.OrigemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelo.Entity<EventoDeCobranca>(evento =>
        {
            /*
             * A chave é o identificador do evento no PSP, e é o que impede
             * baixa dupla: duas entregas simultâneas do mesmo aviso viram uma
             * inserção que passa e outra que o banco recusa. Uma checagem no
             * código perderia exatamente esse caso.
             *
             * <b>E o tenant entra na chave.</b> O identificador do evento é
             * único dentro de uma conta do PSP, e cada escritório tem a conta
             * dele — o dinheiro cai na conta bancária dele. Chave só pelo id
             * faria o evento de um escritório calar o evento homônimo de outro,
             * e o segundo escritório ficaria sem a baixa de um pagamento que
             * aconteceu, sem erro nenhum aparecendo em lugar nenhum.
             */
            evento.HasKey(e => new { e.TenantId, e.Id });
            evento.Property(e => e.Id).HasMaxLength(200);
            evento.Property(e => e.Tipo).HasMaxLength(60);
            evento.Property(e => e.Divergencia).HasMaxLength(300);
            evento.Property(e => e.RecebidoEm).HasDefaultValueSql("now()");

            evento.HasIndex(e => new { e.TenantId, e.LancamentoId });
        });

        modelo.Entity<ContaBancaria>(conta =>
        {
            conta.HasKey(c => c.Id);
            conta.Property(c => c.Nome).HasMaxLength(60);
            conta.Property(c => c.Banco).HasMaxLength(60);
            conta.Property(c => c.Agencia).HasMaxLength(10);
            conta.Property(c => c.Numero).HasMaxLength(20);
            conta.Property(c => c.SaldoInicial).HasPrecision(14, 2);
            conta.Property(c => c.CriadoEm).HasDefaultValueSql("now()");
            conta.Property(c => c.AtualizadoEm).HasDefaultValueSql("now()");

            /*
             * Ativa fica sem valor padrão no banco, de propósito. Com padrão
             * verdadeiro, o EF deixaria de mandar o falso, que é o valor vazio
             * de bool, e toda conta gravada como inativa voltaria ativa.
             */

            /* O nome é como a conta se escolhe na tela: dois iguais obrigariam a adivinhar. */
            conta.HasIndex(c => new { c.TenantId, c.Nome }).IsUnique();

            /* Uma conta só recebe as cobranças do PSP: com duas, o aviso de pagamento não saberia onde baixar. */
            conta.HasIndex(c => c.TenantId).IsUnique().HasFilter("recebe_cobrancas");
        });

        modelo.Entity<MovimentoDeConta>(movimento =>
        {
            movimento.HasKey(m => m.Id);
            movimento.Property(m => m.Valor).HasPrecision(14, 2);
            movimento.Property(m => m.Descricao).HasMaxLength(200);
            movimento.Property(m => m.Origem).HasMaxLength(30);
            movimento.Property(m => m.CriadoEm).HasDefaultValueSql("now()");

            /* O saldo e o extrato leem por conta e por data. */
            movimento.HasIndex(m => new { m.TenantId, m.ContaId, m.Data });

            /*
             * Uma baixa viva, um movimento. Se duas gravações da mesma baixa
             * passassem, o saldo contaria o mesmo dinheiro duas vezes; quem
             * recusa a segunda é o banco. A baixa que o PSP estornou muda de
             * origem e sai deste índice, para o pagamento seguinte ter onde entrar.
             */
            movimento.HasIndex(m => m.LancamentoId)
                .IsUnique()
                .HasFilter("lancamento_id IS NOT NULL AND origem = 'baixa'");

            /* As duas pontas de uma transferência se acham pelo identificador dela, e se apagam juntas. */
            movimento.HasIndex(m => m.TransferenciaId);

            /* Restrict: conta e lançamento com dinheiro passado por eles não somem de baixo do movimento. */
            movimento.HasOne(m => m.Conta)
                .WithMany()
                .HasForeignKey(m => m.ContaId)
                .OnDelete(DeleteBehavior.Restrict);

            movimento.HasOne<Lancamento>()
                .WithMany()
                .HasForeignKey(m => m.LancamentoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelo.Entity<Categoria>(categoria =>
        {
            categoria.HasKey(c => c.Id);
            categoria.Property(c => c.Nome).HasMaxLength(60);
            categoria.Property(c => c.CriadoEm).HasDefaultValueSql("now()");
            categoria.Property(c => c.AtualizadoEm).HasDefaultValueSql("now()");

            /* A árvore se monta lendo as filhas de cada uma. */
            categoria.HasIndex(c => new { c.TenantId, c.PaiId });

            /* Restrict: a de cima não some de baixo das filhas. Categoria não se apaga, inativa. */
            categoria.HasOne<Categoria>()
                .WithMany()
                .HasForeignKey(c => c.PaiId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelo.Entity<CentroDeCusto>(centro =>
        {
            centro.HasKey(c => c.Id);
            centro.Property(c => c.Nome).HasMaxLength(60);
            centro.Property(c => c.CriadoEm).HasDefaultValueSql("now()");
            centro.Property(c => c.AtualizadoEm).HasDefaultValueSql("now()");

            /* O nome é como o centro se escolhe na tela: dois iguais obrigariam a adivinhar. */
            centro.HasIndex(c => new { c.TenantId, c.Nome }).IsUnique();
        });

        modelo.Entity<Recorrencia>(recorrencia =>
        {
            recorrencia.HasKey(r => r.Id);
            recorrencia.Property(r => r.Descricao).HasMaxLength(200);
            recorrencia.Property(r => r.Valor).HasPrecision(14, 2);
            recorrencia.Property(r => r.CriadoEm).HasDefaultValueSql("now()");
            recorrencia.Property(r => r.AtualizadoEm).HasDefaultValueSql("now()");

            /*
             * Ativa fica sem valor padrão no banco, pelo mesmo motivo da conta
             * bancária: com padrão verdadeiro, o EF deixaria de mandar o falso, e a
             * recorrência gravada como inativa voltaria ativa.
             */

            /* A geração lê as do escritório, e a lista as ordena por natureza. */
            recorrencia.HasIndex(r => new { r.TenantId, r.Natureza, r.Ativa });

            /* Restrict: pessoa, categoria e centro não somem de baixo de uma recorrência. */
            recorrencia.HasOne(r => r.Pessoa)
                .WithMany()
                .HasForeignKey(r => r.PessoaId)
                .OnDelete(DeleteBehavior.Restrict);

            recorrencia.HasOne(r => r.Categoria)
                .WithMany()
                .HasForeignKey(r => r.CategoriaId)
                .OnDelete(DeleteBehavior.Restrict);

            recorrencia.HasOne(r => r.CentroDeCusto)
                .WithMany()
                .HasForeignKey(r => r.CentroDeCustoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelo.Entity<EventoDeAuditoria>(evento =>
        {
            evento.HasKey(e => e.Id);
            evento.Property(e => e.Mudancas).HasColumnType("jsonb");
            evento.Property(e => e.Autor).HasMaxLength(200);

            /*
             * Sem chave estrangeira nenhuma, de propósito: a trilha conta também o
             * que deixou de existir, como o movimento apagado pelo estorno. Quem
             * recusa alterar e apagar evento é um gatilho, escrito na migração.
             */

            /* O histórico lê por lançamento ou por conta, na ordem em que aconteceu. */
            evento.HasIndex(e => new { e.TenantId, e.LancamentoId, e.Em });
            evento.HasIndex(e => new { e.TenantId, e.ContaId, e.Em });
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
