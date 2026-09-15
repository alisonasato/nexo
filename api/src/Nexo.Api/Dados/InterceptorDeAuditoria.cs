using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nexo.Api.Dominio;

namespace Nexo.Api.Dados;

/// <summary>
/// Escreve a trilha de auditoria de toda gravação que mexe em dinheiro:
/// lançamentos, movimentos e contas.
///
/// <para>
/// <b>No interceptor, e não em cada endpoint.</b> Baixar, estornar, cancelar,
/// renegociar, lançar, transferir e o aviso do PSP gravam em mais de uma dúzia
/// de lugares. Um registro escrito à mão em cada um falharia do jeito mais
/// provável: não por estar errado, mas por não estar lá no próximo endpoint.
/// Aqui, nada que passe pelo <c>SaveChanges</c> escapa.
/// </para>
/// <para>
/// <b>Na mesma gravação da mudança.</b> Os eventos entram na própria transação:
/// se ela falha, a trilha não conta o que não aconteceu, e se ela passa, a
/// trilha não tem como ter ficado para trás.
/// </para>
/// <para>
/// O que não passa pelo <c>SaveChanges</c> não é visto. <c>ExecuteUpdate</c> e
/// <c>ExecuteDelete</c> vão direto ao banco; quem os usa sobre estas tabelas
/// escreve o evento à mão, e este interceptor só assina por ele.
/// </para>
/// </summary>
public sealed class InterceptorDeAuditoria(IHttpContextAccessor acessor) : SaveChangesInterceptor
{
    /// <summary>Onde uma requisição sem sessão diz, para a trilha, quem ela é.</summary>
    public const string ChaveDoAutor = "nexo.autor";

    /// <summary>Quem assina o que não veio de requisição nenhuma.</summary>
    public const string Sistema = "Sistema";

    /* Carimbos que mudam em toda gravação e não contam nada que o evento já não conte. */
    private static readonly HashSet<string> Ignorados =
        [nameof(Lancamento.Id), nameof(Lancamento.TenantId), nameof(Lancamento.CriadoEm), nameof(Lancamento.AtualizadoEm)];

    /* Os eventos escritos para a gravação em curso. */
    private readonly List<EventoDeAuditoria> _escritos = [];

    /// <summary>
    /// Diz quem assina uma requisição que não tem sessão, como o aviso do PSP.
    /// Numa requisição com sessão, quem assina é a pessoa, e nada gravado aqui
    /// troca isso.
    /// </summary>
    public static void FixarAutor(HttpContext contexto, string autor) =>
        contexto.Items[ChaveDoAutor] = autor;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData dados,
        InterceptionResult<int> resultado)
    {
        Escrever(dados.Context);
        return resultado;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData dados,
        InterceptionResult<int> resultado,
        CancellationToken cancelamento = default)
    {
        Escrever(dados.Context);
        return ValueTask.FromResult(resultado);
    }

    private void Escrever(DbContext? contexto)
    {
        if (contexto is null) return;

        /*
         * Uma gravação que falhou deixa os eventos dela pendurados no contexto, e
         * quem segue gravando no mesmo contexto, como a baixa em lote, somaria uma
         * segunda cópia. Os eventos se refazem do zero, a partir do que está
         * pendente agora.
         */
        foreach (var anterior in _escritos)
        {
            var entrada = contexto.Entry(anterior);
            if (entrada.State == EntityState.Added) entrada.State = EntityState.Detached;
        }

        _escritos.Clear();

        contexto.ChangeTracker.DetectChanges();

        var (usuarioId, autor) = QuemFez();
        var agora = DateTimeOffset.UtcNow;

        foreach (var entrada in contexto.ChangeTracker.Entries().ToList())
        {
            if (entrada.Entity is EventoDeAuditoria escritoAMao)
            {
                if (entrada.State == EntityState.Added && escritoAMao.Autor.Length == 0)
                    Assinar(escritoAMao, usuarioId, autor, agora);
                continue;
            }

            if (Descrever(entrada) is not { } evento) continue;

            Assinar(evento, usuarioId, autor, agora);
            contexto.Add(evento);
            _escritos.Add(evento);
        }
    }

    private static void Assinar(EventoDeAuditoria evento, Guid? usuarioId, string autor, DateTimeOffset agora)
    {
        evento.UsuarioId = usuarioId;
        evento.Autor = autor;
        evento.Em = agora;
    }

    /// <summary>Quem está gravando: a pessoa da sessão, quem a requisição disse ser, ou o sistema.</summary>
    private (Guid? UsuarioId, string Autor) QuemFez()
    {
        var http = acessor.HttpContext;
        if (http is null) return (null, Sistema);

        if (Guid.TryParse(http.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var usuario))
            return (usuario, http.User.FindFirst(JwtRegisteredClaimNames.Email)?.Value ?? Sistema);

        return http.Items.TryGetValue(ChaveDoAutor, out var fixado) && fixado is string nome
            ? (null, nome)
            : (null, Sistema);
    }

    /// <summary>O evento de uma entrada; nulo quando ela não é auditada ou quando nada mudou de fato.</summary>
    private static EventoDeAuditoria? Descrever(EntityEntry entrada)
    {
        AcaoDeAuditoria? acao = entrada.State switch
        {
            EntityState.Added => AcaoDeAuditoria.Criado,
            EntityState.Modified => AcaoDeAuditoria.Alterado,
            EntityState.Deleted => AcaoDeAuditoria.Apagado,
            _ => null,
        };

        if (acao is null) return null;

        EventoDeAuditoria? evento = entrada.Entity switch
        {
            Lancamento lancamento => new EventoDeAuditoria
            {
                TenantId = lancamento.TenantId,
                Entidade = EntidadeAuditada.Lancamento,
                EntidadeId = lancamento.Id,
                LancamentoId = lancamento.Id,
            },
            MovimentoDeConta movimento => new EventoDeAuditoria
            {
                TenantId = movimento.TenantId,
                Entidade = EntidadeAuditada.MovimentoDeConta,
                EntidadeId = movimento.Id,
                LancamentoId = movimento.LancamentoId,
                ContaId = movimento.ContaId,
            },
            ContaBancaria conta => new EventoDeAuditoria
            {
                TenantId = conta.TenantId,
                Entidade = EntidadeAuditada.ContaBancaria,
                EntidadeId = conta.Id,
                ContaId = conta.Id,
            },
            _ => null,
        };

        if (evento is null) return null;

        var mudancas = new List<MudancaDeCampo>();

        foreach (var propriedade in entrada.Properties)
        {
            if (Ignorados.Contains(propriedade.Metadata.Name)) continue;

            var antes = acao == AcaoDeAuditoria.Criado ? null : Texto(propriedade.OriginalValue);
            var depois = acao == AcaoDeAuditoria.Apagado ? null : Texto(propriedade.CurrentValue);

            /* Campo regravado com o mesmo valor não é mudança. */
            if (antes == depois) continue;

            mudancas.Add(new MudancaDeCampo(
                JsonNamingPolicy.CamelCase.ConvertName(propriedade.Metadata.Name), antes, depois));
        }

        /* Uma gravação que só trocou o carimbo de atualização não aconteceu, para quem lê a trilha. */
        if (acao == AcaoDeAuditoria.Alterado && mudancas.Count == 0) return null;

        evento.Id = Guid.NewGuid();
        evento.Acao = acao.Value;
        evento.Mudancas = EventoDeAuditoria.Escrever(mudancas);
        return evento;
    }

    /// <summary>
    /// O valor em texto neutro, sem cultura nem fuso. Todo decimal destas tabelas
    /// é dinheiro, com duas casas: 450 e 450,00 lidos do banco saem iguais, e não
    /// viram uma mudança que não houve.
    /// </summary>
    private static string? Texto(object? valor) => valor switch
    {
        null => null,
        string texto => texto.Length == 0 ? null : texto,
        decimal numero => numero.ToString("0.00", CultureInfo.InvariantCulture),
        DateOnly data => data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset momento => momento.ToString("O", CultureInfo.InvariantCulture),
        bool sim => sim ? "true" : "false",
        Enum enumeracao => enumeracao.ToString(),
        IFormattable formatavel => formatavel.ToString(null, CultureInfo.InvariantCulture),
        _ => valor.ToString(),
    };
}
