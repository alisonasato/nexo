using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dados;
using Nexo.Api.Dominio;

namespace Nexo.Api.Endpoints;

/// <summary>
/// O que o escritório tem a receber, e a baixa quando o dinheiro entra.
/// </summary>
public static class Recebiveis
{
    public static IEndpointRouteBuilder MapRecebiveis(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/recebiveis").WithTags("Recebíveis");

        grupo.MapGet("/", Listar)
            .WithName("ListarRecebiveis")
            .WithSummary("Lista os recebíveis")
            .Produces<ResumoDeRecebiveis>();

        grupo.MapPost("/{id:guid}/baixar", Baixar)
            .WithName("BaixarRecebivel")
            .WithSummary("Registra o recebimento")
            .Produces<RecebivelNaLista>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapPost("/{id:guid}/estornar", Estornar)
            .WithName("EstornarRecebivel")
            .WithSummary("Desfaz a baixa")
            .WithDescription("Existe porque baixa errada acontece, e sem estorno a correção viraria um segundo registro inventado.")
            .Produces<RecebivelNaLista>()
            .Produces(StatusCodes.Status404NotFound);

        return rotas;
    }

    private static async Task<IResult> Listar(
        NexoDbContext banco,
        CancellationToken cancelamento,
        [FromQuery] SituacaoRecebivel? situacao = null,
        [FromQuery] int? ano = null,
        [FromQuery] int? mes = null)
    {
        var consulta = banco.Recebiveis.AsNoTracking();

        if (situacao is { } filtro) consulta = consulta.Where(r => r.Situacao == filtro);
        if (ano is { } a) consulta = consulta.Where(r => r.CompetenciaAno == a);
        if (mes is { } m) consulta = consulta.Where(r => r.CompetenciaMes == m);

        var itens = await consulta
            .OrderBy(recebivel => recebivel.Vencimento)
            .ThenBy(recebivel => recebivel.Cliente!.Codigo)
            .Select(recebivel => new RecebivelNaLista(
                recebivel.Id,
                recebivel.Cliente!.Codigo,
                recebivel.Cliente.Pessoa!.Nome,
                recebivel.Descricao,
                recebivel.CompetenciaAno,
                recebivel.CompetenciaMes,
                recebivel.Valor,
                recebivel.Vencimento,
                recebivel.Situacao,
                recebivel.ValorPago,
                recebivel.PagoEm,
                recebivel.OrigemDaBaixa))
            .ToListAsync(cancelamento);

        var hoje = DateOnly.FromDateTime(DateTime.Today);

        /*
         * Os totais são derivados da lista, não guardados em lugar nenhum.
         * Total gravado é total que fica errado: basta uma baixa mexer no
         * recebível e esquecer de mexer no somatório.
         */
        var aberto = itens.Where(i => i.Situacao == SituacaoRecebivel.Aberto).ToList();

        return Results.Ok(new ResumoDeRecebiveis(
            itens,
            itens.Count,
            aberto.Sum(i => i.Valor),
            aberto.Where(i => i.Vencimento < hoje).Sum(i => i.Valor),
            itens.Where(i => i.Situacao == SituacaoRecebivel.Pago).Sum(i => i.ValorPago ?? 0m)));
    }

    private static async Task<IResult> Baixar(
        Guid id,
        [FromBody] DadosDaBaixa dados,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var recebivel = await banco.Recebiveis
            .Include(r => r.Cliente).ThenInclude(c => c!.Pessoa)
            .FirstOrDefaultAsync(r => r.Id == id, cancelamento);

        if (recebivel is null) return Results.NotFound();

        if (recebivel.Situacao == SituacaoRecebivel.Pago)
        {
            /*
             * Recusar em vez de sobrescrever. Baixar duas vezes costuma ser
             * clique repetido, e sobrescrever apagaria a data e o valor da
             * primeira baixa — que é exatamente o que alguém vai procurar
             * quando a conciliação não fechar.
             */
            return Problema("id", "Recebível já baixado",
                $"Este recebível foi baixado em {recebivel.PagoEm:dd/MM/yyyy}.",
                "Para corrigir o valor ou a data, estorne a baixa e registre de novo.");
        }

        if (recebivel.Situacao == SituacaoRecebivel.Cancelado)
        {
            return Problema("id", "Recebível cancelado",
                "Um recebível cancelado não pode ser baixado.",
                "Se a cobrança voltou a valer, gere-a novamente.");
        }

        if (dados.ValorPago <= 0)
        {
            return Problema("valorPago", "Valor inválido",
                "O valor recebido precisa ser maior que zero.",
                "Informe quanto entrou de fato — pode ser diferente do valor cobrado.");
        }

        recebivel.Situacao = SituacaoRecebivel.Pago;
        recebivel.ValorPago = dados.ValorPago;
        recebivel.PagoEm = dados.PagoEm ?? DateOnly.FromDateTime(DateTime.Today);
        recebivel.OrigemDaBaixa = OrigensDeBaixa.Manual;
        recebivel.AtualizadoEm = DateTimeOffset.UtcNow;

        await banco.SaveChangesAsync(cancelamento);

        return Results.Ok(Detalhar(recebivel));
    }

    private static async Task<IResult> Estornar(Guid id, NexoDbContext banco, CancellationToken cancelamento)
    {
        var recebivel = await banco.Recebiveis
            .Include(r => r.Cliente).ThenInclude(c => c!.Pessoa)
            .FirstOrDefaultAsync(r => r.Id == id, cancelamento);

        if (recebivel is null) return Results.NotFound();

        recebivel.Situacao = SituacaoRecebivel.Aberto;
        recebivel.ValorPago = null;
        recebivel.PagoEm = null;
        recebivel.OrigemDaBaixa = string.Empty;
        recebivel.AtualizadoEm = DateTimeOffset.UtcNow;

        await banco.SaveChangesAsync(cancelamento);

        return Results.Ok(Detalhar(recebivel));
    }

    private static RecebivelNaLista Detalhar(Recebivel recebivel) => new(
        recebivel.Id,
        recebivel.Cliente!.Codigo,
        recebivel.Cliente.Pessoa!.Nome,
        recebivel.Descricao,
        recebivel.CompetenciaAno,
        recebivel.CompetenciaMes,
        recebivel.Valor,
        recebivel.Vencimento,
        recebivel.Situacao,
        recebivel.ValorPago,
        recebivel.PagoEm,
        recebivel.OrigemDaBaixa);

    private static IResult Problema(string campo, string titulo, string descricao, string sugestao) =>
        Results.Json(
            new RespostaComProblemas([new Problema(campo, titulo, descricao, sugestao)]),
            statusCode: 422);
}

public record DadosDaBaixa(decimal ValorPago, DateOnly? PagoEm);

public record RecebivelNaLista(
    Guid Id,
    string CodigoDoCliente,
    string NomeDoCliente,
    string Descricao,
    int CompetenciaAno,
    int CompetenciaMes,
    decimal Valor,
    DateOnly Vencimento,
    SituacaoRecebivel Situacao,
    decimal? ValorPago,
    DateOnly? PagoEm,
    string OrigemDaBaixa);

/// <param name="TotalEmAberto">Soma do que ainda não entrou.</param>
/// <param name="TotalVencido">Parte do aberto cujo vencimento já passou.</param>
/// <param name="TotalRecebido">Soma do que de fato entrou, e não do que era devido.</param>
public record ResumoDeRecebiveis(
    List<RecebivelNaLista> Itens,
    int Quantidade,
    decimal TotalEmAberto,
    decimal TotalVencido,
    decimal TotalRecebido);
