using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dados;
using Nexo.Api.Dominio;
using Npgsql;

namespace Nexo.Api.Endpoints;

public static class Contratos
{
    public static IEndpointRouteBuilder MapContratos(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/contratos").WithTags("Contratos");

        grupo.MapGet("/", Listar)
            .WithName("ListarContratos")
            .WithSummary("Lista os contratos recorrentes")
            .Produces<List<ContratoNaLista>>();

        grupo.MapGet("/{id:guid}", Obter)
            .WithName("ObterContrato")
            .WithSummary("Abre um contrato")
            .Produces<ContratoDetalhado>()
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapPost("/", Criar)
            .WithName("CriarContrato")
            .WithSummary("Cria um contrato")
            .Produces<ContratoDetalhado>(StatusCodes.Status201Created)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPut("/{id:guid}", Alterar)
            .WithName("AlterarContrato")
            .WithSummary("Altera um contrato")
            .Produces<ContratoDetalhado>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapPost("/gerar-mensalidades", GerarMensalidades)
            .WithName("GerarMensalidades")
            .WithSummary("Gera as mensalidades de uma competência")
            .WithDescription("Pode ser executado quantas vezes for preciso: o que já foi gerado é ignorado, não duplicado.")
            .Produces<ResultadoDaGeracao>();

        return rotas;
    }

    private static async Task<IResult> Listar(NexoDbContext banco, CancellationToken cancelamento)
    {
        var contratos = await banco.Contratos.AsNoTracking()
            .OrderBy(contrato => contrato.Codigo)
            .Select(contrato => new ContratoNaLista(
                contrato.Id,
                contrato.Codigo,
                contrato.ClienteId,
                contrato.Cliente!.Codigo,
                contrato.Cliente.Pessoa!.Nome,
                contrato.Descricao,
                contrato.Valor,
                contrato.DiaDeVencimento,
                contrato.Situacao))
            .ToListAsync(cancelamento);

        return Results.Ok(contratos);
    }

    private static async Task<IResult> Obter(Guid id, NexoDbContext banco, CancellationToken cancelamento)
    {
        var contrato = await banco.Contratos.AsNoTracking()
            .FirstOrDefaultAsync(contrato => contrato.Id == id, cancelamento);

        return contrato is null ? Results.NotFound() : Results.Ok(Detalhar(contrato));
    }

    private static async Task<IResult> Criar(
        [FromBody] DadosDeContrato dados,
        NexoDbContext banco,
        IContextoDeTenant contexto,
        CancellationToken cancelamento)
    {
        if (contexto.TenantAtual is not { } tenant) return Results.Unauthorized();

        var problemas = await Conferir(dados, banco, cancelamento);
        if (problemas.Count > 0)
            return Results.Json(new RespostaComProblemas(problemas), statusCode: 422);

        var codigos = await banco.Contratos.AsNoTracking()
            .Select(contrato => contrato.Codigo)
            .ToListAsync(cancelamento);

        var contrato = new Contrato
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            Codigo = Codigos.Proximo("C", codigos),
            CriadoEm = DateTimeOffset.UtcNow,
            AtualizadoEm = DateTimeOffset.UtcNow,
        };

        Aplicar(dados, contrato);

        banco.Contratos.Add(contrato);
        await banco.SaveChangesAsync(cancelamento);

        return Results.Created($"/contratos/{contrato.Id}", Detalhar(contrato));
    }

    private static async Task<IResult> Alterar(
        Guid id,
        [FromBody] DadosDeContrato dados,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var contrato = await banco.Contratos.FirstOrDefaultAsync(c => c.Id == id, cancelamento);
        if (contrato is null) return Results.NotFound();

        var problemas = await Conferir(dados, banco, cancelamento);
        if (problemas.Count > 0)
            return Results.Json(new RespostaComProblemas(problemas), statusCode: 422);

        Aplicar(dados, contrato);
        contrato.AtualizadoEm = DateTimeOffset.UtcNow;

        await banco.SaveChangesAsync(cancelamento);

        return Results.Ok(Detalhar(contrato));
    }

    /// <summary>
    /// Gera as mensalidades de uma competência.
    ///
    /// <para>
    /// É <b>idempotente</b>, e essa é a característica que importa: o
    /// escritório vai clicar duas vezes, ou duas pessoas vão clicar ao mesmo
    /// tempo no fim do mês. Quem garante isso não é a verificação abaixo — é o
    /// índice único de <c>contrato + competência</c> no banco. A verificação
    /// serve para dar uma resposta boa; o índice serve para o dado nunca ficar
    /// errado, inclusive quando duas transações correm juntas.
    /// </para>
    /// </summary>
    private static async Task<IResult> GerarMensalidades(
        [FromBody] PedidoDeGeracao pedido,
        NexoDbContext banco,
        IContextoDeTenant contexto,
        CancellationToken cancelamento)
    {
        if (contexto.TenantAtual is not { } tenant) return Results.Unauthorized();

        if (pedido.Mes is < 1 or > 12)
        {
            return Results.Json(new RespostaComProblemas([new Problema(
                "mes", "Competência inválida",
                $"O mês informado foi {pedido.Mes}.",
                "Informe um mês entre 1 e 12.")]), statusCode: 422);
        }

        var candidatos = await banco.Contratos.AsNoTracking()
            .Where(contrato => pedido.ContratoIds == null || pedido.ContratoIds.Contains(contrato.Id))
            .ToListAsync(cancelamento);

        /*
         * Cancelados ficam de fora da conta, do mesmo jeito que ficam de fora
         * do índice único. É o que permite corrigir: cancela a mensalidade
         * errada e gera de novo, com o valor certo, na mesma competência.
         */
        var jaGerados = await banco.Recebiveis.AsNoTracking()
            .Where(recebivel =>
                recebivel.CompetenciaAno == pedido.Ano
                && recebivel.CompetenciaMes == pedido.Mes
                && recebivel.ContratoId != null
                && recebivel.Situacao != SituacaoRecebivel.Cancelado)
            .Select(recebivel => recebivel.ContratoId!.Value)
            .ToListAsync(cancelamento);

        var geradas = 0;
        var ignoradas = 0;
        var foraDeVigencia = 0;

        foreach (var contrato in candidatos)
        {
            if (!contrato.VigenteEm(pedido.Ano, pedido.Mes)) { foraDeVigencia++; continue; }
            if (jaGerados.Contains(contrato.Id)) { ignoradas++; continue; }

            banco.Recebiveis.Add(new Recebivel
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                ClienteId = contrato.ClienteId,
                ContratoId = contrato.Id,
                CompetenciaAno = pedido.Ano,
                CompetenciaMes = pedido.Mes,
                Descricao = contrato.Descricao,
                Valor = contrato.Valor,
                Vencimento = contrato.VencimentoEm(pedido.Ano, pedido.Mes),
                Situacao = SituacaoRecebivel.Aberto,
                CriadoEm = DateTimeOffset.UtcNow,
                AtualizadoEm = DateTimeOffset.UtcNow,
            });

            geradas++;
        }

        try
        {
            await banco.SaveChangesAsync(cancelamento);
        }
        catch (DbUpdateException erro) when (erro.InnerException is PostgresException { SqlState: "23505" })
        {
            /*
             * Alguém gerou a mesma competência entre a leitura e a gravação. É
             * o índice único fazendo o trabalho dele. Não é erro do usuário:
             * o resultado desejado — mensalidade existir uma vez — já está no
             * banco.
             */
            return Results.Ok(new ResultadoDaGeracao(0, geradas + ignoradas, foraDeVigencia,
                "As mensalidades desta competência já haviam sido geradas."));
        }

        var recado = geradas == 0
            ? "Nada a gerar: nenhuma mensalidade nova para esta competência."
            : geradas == 1
                ? "1 mensalidade gerada."
                : $"{geradas} mensalidades geradas.";

        return Results.Ok(new ResultadoDaGeracao(geradas, ignoradas, foraDeVigencia, recado));
    }

    /* ------------------------------------------------------------- apoio */

    private static async Task<List<Problema>> Conferir(
        DadosDeContrato dados,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var problemas = new List<Problema>();

        var cliente = await banco.Clientes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == dados.ClienteId, cancelamento);

        if (cliente is null)
        {
            problemas.Add(new Problema("clienteId", "Contrato sem cliente",
                "O cliente informado não existe.",
                "Escolha um cliente da lista. Se ele ainda não é cliente, crie o vínculo primeiro."));
        }

        if (string.IsNullOrWhiteSpace(dados.Descricao))
        {
            problemas.Add(new Problema("descricao", "Falha na validação do contrato",
                "A descrição não foi informada.",
                "Diga o que está sendo cobrado: “Honorários contábeis”, “Folha de pagamento”."));
        }

        if (dados.Valor <= 0)
        {
            problemas.Add(new Problema("valor", "Falha na validação do contrato",
                "O valor precisa ser maior que zero.",
                "Informe quanto o cliente paga por mês."));
        }

        if (dados.DiaDeVencimento is < 1 or > 31)
        {
            problemas.Add(new Problema("diaDeVencimento", "Falha na validação do contrato",
                $"O dia de vencimento informado foi {dados.DiaDeVencimento}.",
                "Informe um dia entre 1 e 31. Meses mais curtos usam o último dia."));
        }

        if (dados.FimDaVigencia is { } fim && fim < dados.InicioDaVigencia)
        {
            problemas.Add(new Problema("fimDaVigencia", "Vigência invertida",
                "O fim da vigência é anterior ao início.",
                "Confira as duas datas: o fim precisa vir depois do início, ou ficar em branco."));
        }

        return problemas;
    }

    private static void Aplicar(DadosDeContrato dados, Contrato contrato)
    {
        contrato.ClienteId = dados.ClienteId;
        contrato.Descricao = (dados.Descricao ?? string.Empty).Trim();
        contrato.Valor = dados.Valor;
        contrato.DiaDeVencimento = dados.DiaDeVencimento;
        contrato.InicioDaVigencia = dados.InicioDaVigencia;
        contrato.FimDaVigencia = dados.FimDaVigencia;
        contrato.Situacao = dados.Situacao;
        contrato.Observacoes = (dados.Observacoes ?? string.Empty).Trim();
    }

    private static ContratoDetalhado Detalhar(Contrato contrato) => new(
        contrato.Id,
        contrato.Codigo,
        contrato.ClienteId,
        contrato.Descricao,
        contrato.Valor,
        contrato.DiaDeVencimento,
        contrato.InicioDaVigencia,
        contrato.FimDaVigencia,
        contrato.Situacao,
        contrato.Observacoes);
}

public record DadosDeContrato(
    Guid ClienteId,
    string? Descricao,
    decimal Valor,
    int DiaDeVencimento,
    DateOnly InicioDaVigencia,
    DateOnly? FimDaVigencia,
    SituacaoContrato Situacao,
    string? Observacoes);

public record ContratoNaLista(
    Guid Id,
    string Codigo,
    Guid ClienteId,
    string CodigoDoCliente,
    string NomeDoCliente,
    string Descricao,
    decimal Valor,
    int DiaDeVencimento,
    SituacaoContrato Situacao);

public record ContratoDetalhado(
    Guid Id,
    string Codigo,
    Guid ClienteId,
    string Descricao,
    decimal Valor,
    int DiaDeVencimento,
    DateOnly InicioDaVigencia,
    DateOnly? FimDaVigencia,
    SituacaoContrato Situacao,
    string Observacoes);

/// <param name="ContratoIds">Nulo para gerar de todos os contratos vigentes.</param>
public record PedidoDeGeracao(int Ano, int Mes, List<Guid>? ContratoIds);

/// <param name="Geradas">Mensalidades criadas agora.</param>
/// <param name="Ignoradas">Já existiam nesta competência.</param>
/// <param name="ForaDeVigencia">Contratos suspensos, encerrados ou fora do período.</param>
public record ResultadoDaGeracao(int Geradas, int Ignoradas, int ForaDeVigencia, string Recado);
