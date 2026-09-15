using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dados;
using Nexo.Api.Dominio;

namespace Nexo.Api.Endpoints;

/// <summary>
/// O histórico de um lançamento e o de uma conta, lidos da trilha de auditoria.
/// </summary>
public static class Historico
{
    public static IEndpointRouteBuilder MapHistorico(this IEndpointRouteBuilder rotas)
    {
        /* Caminho inteiro, e não grupo: os dois terminam igual, e o analisador não soma o prefixo do grupo. */
        rotas.MapGet("/lancamentos/{id:guid}/historico", DoLancamento)
            .WithTags("Lançamentos")
            .WithName("HistoricoDoLancamento")
            .WithSummary("Quem fez o quê no lançamento, e quando")
            .WithDescription("Do mais antigo ao mais novo. Traz também os movimentos que as baixas gravaram na conta, inclusive os que o estorno apagou.")
            .Produces<List<EventoNoHistorico>>()
            .Produces(StatusCodes.Status404NotFound);

        rotas.MapGet("/contas-bancarias/{id:guid}/historico", DaConta)
            .WithTags("Contas bancárias")
            .WithName("HistoricoDaContaBancaria")
            .WithSummary("Quem fez o quê na conta, e quando")
            .WithDescription("Do mais antigo ao mais novo. Traz as mudanças no cadastro, como o saldo inicial e a marca de receber as cobranças, e todo movimento que entrou ou saiu dela, inclusive os apagados.")
            .Produces<List<EventoNoHistorico>>()
            .Produces(StatusCodes.Status404NotFound);

        return rotas;
    }

    private static async Task<IResult> DoLancamento(
        Guid id,
        NexoDbContext banco,
        IContextoDeTenant contexto,
        CancellationToken cancelamento)
    {
        if (!await banco.Lancamentos.AnyAsync(lancamento => lancamento.Id == id, cancelamento))
            return Results.NotFound();

        return Results.Ok(await Ler(
            banco.EventosDeAuditoria.Where(evento => evento.LancamentoId == id), banco, contexto, cancelamento));
    }

    private static async Task<IResult> DaConta(
        Guid id,
        NexoDbContext banco,
        IContextoDeTenant contexto,
        CancellationToken cancelamento)
    {
        if (!await banco.ContasBancarias.AnyAsync(conta => conta.Id == id, cancelamento))
            return Results.NotFound();

        return Results.Ok(await Ler(
            banco.EventosDeAuditoria.Where(evento => evento.ContaId == id), banco, contexto, cancelamento));
    }

    private static async Task<List<EventoNoHistorico>> Ler(
        IQueryable<EventoDeAuditoria> eventos,
        NexoDbContext banco,
        IContextoDeTenant contexto,
        CancellationToken cancelamento)
    {
        /* Uma gravação escreve seus eventos no mesmo instante: o lançamento vem antes do movimento dele. */
        var lidos = await eventos.AsNoTracking()
            .OrderBy(evento => evento.Em)
            .ThenBy(evento => evento.Entidade)
            .ThenBy(evento => evento.Id)
            .ToListAsync(cancelamento);

        /*
         * O nome de quem fez sai do cadastro de hoje, e o e-mail guardado fica para
         * quem já não está nele. A tabela de usuários não tem política de
         * isolamento, e por isso o escritório entra no filtro por escrito.
         */
        var usuarios = lidos
            .Where(evento => evento.UsuarioId is not null)
            .Select(evento => evento.UsuarioId!.Value)
            .Distinct()
            .ToList();

        var tenant = contexto.TenantAtual;
        var nomes = usuarios.Count == 0 || tenant is null
            ? []
            : await banco.Users.AsNoTracking()
                .Where(usuario => usuario.TenantId == tenant && usuarios.Contains(usuario.Id))
                .ToDictionaryAsync(usuario => usuario.Id, usuario => usuario.Nome, cancelamento);

        return lidos.Select(evento =>
        {
            var mudancas = evento.LerMudancas();
            var autor = evento.UsuarioId is { } usuario
                && nomes.TryGetValue(usuario, out var nome)
                && !string.IsNullOrWhiteSpace(nome)
                    ? nome
                    : evento.Autor;

            return new EventoNoHistorico(
                evento.Id, evento.Em, autor, evento.Entidade, evento.Acao,
                Resumir(evento.Entidade, evento.Acao, mudancas), mudancas);
        }).ToList();
    }

    /// <summary>
    /// O que aconteceu, em uma ou duas palavras. Sai dos campos que mudaram: a
    /// situação que foi a paga é uma baixa, a que voltou a aberta é um estorno.
    /// </summary>
    internal static string Resumir(EntidadeAuditada entidade, AcaoDeAuditoria acao, List<MudancaDeCampo> mudancas)
    {
        bool Mudou(string campo) => mudancas.Any(mudanca => mudanca.Campo == campo);
        string? Depois(string campo) => mudancas.FirstOrDefault(mudanca => mudanca.Campo == campo)?.Depois;

        return (entidade, acao) switch
        {
            (EntidadeAuditada.Lancamento, AcaoDeAuditoria.Criado) => "Lançamento criado",

            (EntidadeAuditada.Lancamento, AcaoDeAuditoria.Alterado) when Mudou("situacao") => Depois("situacao") switch
            {
                nameof(SituacaoLancamento.Pago) => "Baixa",
                nameof(SituacaoLancamento.Aberto) => "Estorno",
                nameof(SituacaoLancamento.Cancelado) => "Cancelamento",
                nameof(SituacaoLancamento.Renegociado) => "Renegociação",
                _ => "Alteração",
            },

            (EntidadeAuditada.Lancamento, AcaoDeAuditoria.Alterado) when Mudou("cobrancaId") =>
                Depois("cobrancaId") is null ? "Cobrança retirada" : "Cobrança emitida",

            (EntidadeAuditada.Lancamento, AcaoDeAuditoria.Alterado)
                when mudancas.All(mudanca => mudanca.Campo is "categoriaId" or "centroDeCustoId") => "Classificação",

            (EntidadeAuditada.Lancamento, _) => "Alteração",

            (EntidadeAuditada.MovimentoDeConta, AcaoDeAuditoria.Criado) => "Movimento lançado",
            (EntidadeAuditada.MovimentoDeConta, AcaoDeAuditoria.Apagado) => "Movimento apagado",
            (EntidadeAuditada.MovimentoDeConta, _) => "Movimento alterado",

            (EntidadeAuditada.ContaBancaria, AcaoDeAuditoria.Criado) => "Conta cadastrada",
            _ => "Conta alterada",
        };
    }
}

/// <param name="Autor">O nome de quem fez, pelo cadastro de hoje; o e-mail guardado, para quem já saiu dele; "Asaas" no aviso do PSP.</param>
/// <param name="Resumo">O que aconteceu, em uma ou duas palavras: baixa, estorno, cancelamento, movimento apagado.</param>
/// <param name="Mudancas">Os campos que mudaram, com os valores em texto neutro: número com ponto, data ISO, enumeração pelo nome.</param>
public record EventoNoHistorico(
    Guid Id,
    DateTimeOffset Em,
    string Autor,
    EntidadeAuditada Entidade,
    AcaoDeAuditoria Acao,
    string Resumo,
    List<MudancaDeCampo> Mudancas);
