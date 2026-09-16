using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dados;
using Nexo.Api.Dominio;
using Npgsql;

namespace Nexo.Api.Endpoints;

/// <summary>
/// O que se repete sem contrato por trás, e a geração dos lançamentos de uma competência.
/// </summary>
public static class Recorrencias
{
    private const string FalhaNaValidacao = "Falha na validação da recorrência";

    public static IEndpointRouteBuilder MapRecorrencias(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/recorrencias").WithTags("Recorrências");

        grupo.MapGet("/", Listar)
            .WithName("ListarRecorrencias")
            .WithSummary("Lista as recorrências do escritório")
            .WithDescription("A receber antes de a pagar; dentro de cada natureza, as ativas primeiro e pela descrição. Um escritório tem poucas: a lista vem inteira, sem página.")
            .Produces<List<RecorrenciaNaLista>>();

        grupo.MapPost("/", Criar)
            .WithName("CriarRecorrencia")
            .WithSummary("Cadastra uma recorrência")
            .Produces<RecorrenciaNaLista>(StatusCodes.Status201Created)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPut("/{id:guid}", Alterar)
            .WithName("AlterarRecorrencia")
            .WithSummary("Altera uma recorrência")
            .WithDescription("Também inativa e reativa. A natureza não muda depois de criada. O que já foi gerado fica como está: a mudança vale para as próximas gerações.")
            .Produces<RecorrenciaNaLista>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapPost("/gerar", Gerar)
            .WithName("GerarRecorrencias")
            .WithSummary("Gera os lançamentos das recorrências de uma competência")
            .WithDescription("Das duas naturezas de uma vez. Pode ser executado quantas vezes for preciso: o que já foi gerado é ignorado, não duplicado.")
            .Produces<ResultadoDasRecorrencias>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity);

        return rotas;
    }

    private static async Task<IResult> Listar(NexoDbContext banco, CancellationToken cancelamento)
    {
        var recorrencias = await Resumir(banco.Recorrencias.AsNoTracking()
                .OrderBy(recorrencia => recorrencia.Natureza)
                .ThenByDescending(recorrencia => recorrencia.Ativa)
                .ThenBy(recorrencia => recorrencia.Descricao)
                .ThenBy(recorrencia => recorrencia.Id))
            .ToListAsync(cancelamento);

        return Results.Ok(recorrencias);
    }

    private static async Task<IResult> Criar(
        [FromBody] DadosDaRecorrencia dados,
        NexoDbContext banco,
        IContextoDeTenant contexto,
        CancellationToken cancelamento)
    {
        if (contexto.TenantAtual is not { } tenant) return Results.Unauthorized();

        var problemas = await Conferir(dados, dados.Natureza, atual: null, banco, cancelamento);
        if (problemas.Count > 0) return Results.Json(new RespostaComProblemas(problemas), statusCode: 422);

        var agora = DateTimeOffset.UtcNow;
        var recorrencia = new Recorrencia
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            Natureza = dados.Natureza,
            Ativa = true,
            CriadoEm = agora,
        };

        Aplicar(recorrencia, dados, agora);
        banco.Recorrencias.Add(recorrencia);
        await banco.SaveChangesAsync(cancelamento);

        var criada = await Resumir(banco.Recorrencias.AsNoTracking().Where(item => item.Id == recorrencia.Id))
            .SingleAsync(cancelamento);

        return Results.Created($"/recorrencias/{recorrencia.Id}", criada);
    }

    private static async Task<IResult> Alterar(
        Guid id,
        [FromBody] DadosDaRecorrencia dados,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var recorrencia = await banco.Recorrencias.FirstOrDefaultAsync(item => item.Id == id, cancelamento);
        if (recorrencia is null) return Results.NotFound();

        /* A natureza que vale é a gravada: a pedida é ignorada, e a conferência usa a de sempre. */
        var problemas = await Conferir(dados, recorrencia.Natureza, recorrencia, banco, cancelamento);
        if (problemas.Count > 0) return Results.Json(new RespostaComProblemas(problemas), statusCode: 422);

        Aplicar(recorrencia, dados, DateTimeOffset.UtcNow);
        recorrencia.Ativa = dados.Ativa;
        await banco.SaveChangesAsync(cancelamento);

        var alterada = await Resumir(banco.Recorrencias.AsNoTracking().Where(item => item.Id == id))
            .SingleAsync(cancelamento);

        return Results.Ok(alterada);
    }

    /// <summary>
    /// Gera os lançamentos das recorrências que caem na competência.
    ///
    /// <para>
    /// <b>Quem impede gerar duas vezes é o banco.</b> O índice único por
    /// recorrência e competência recusa o segundo lançamento, mesmo quando dois
    /// cliques chegam juntos; a leitura dos já gerados só serve para contar e
    /// para não tentar à toa. Cancelado fica de fora, como nos contratos: é o que
    /// deixa corrigir um lançamento errado gerando de novo.
    /// </para>
    /// </summary>
    private static async Task<IResult> Gerar(
        [FromBody] PedidoDasRecorrencias pedido,
        NexoDbContext banco,
        IContextoDeTenant contexto,
        CancellationToken cancelamento)
    {
        if (contexto.TenantAtual is not { } tenant) return Results.Unauthorized();

        if (pedido.Mes is < 1 or > 12 || pedido.Ano is < 2000 or > 2100)
        {
            return Results.Json(new RespostaComProblemas([new Problema(
                pedido.Mes is < 1 or > 12 ? "mes" : "ano", "Competência inválida",
                $"A competência informada foi {pedido.Mes:00}/{pedido.Ano}.",
                "Informe um mês entre 1 e 12 e um ano entre 2000 e 2100.")]), statusCode: 422);
        }

        var recorrencias = await banco.Recorrencias.AsNoTracking()
            .Include(recorrencia => recorrencia.Categoria)
            .Include(recorrencia => recorrencia.CentroDeCusto)
            .ToListAsync(cancelamento);

        var jaGeradas = await banco.Lancamentos.AsNoTracking()
            .Where(lancamento =>
                lancamento.CompetenciaAno == pedido.Ano
                && lancamento.CompetenciaMes == pedido.Mes
                && lancamento.RecorrenciaId != null
                && lancamento.Situacao != SituacaoLancamento.Cancelado)
            .Select(lancamento => lancamento.RecorrenciaId!.Value)
            .ToListAsync(cancelamento);

        var gerados = 0;
        var ignorados = 0;
        var foraDaVez = 0;
        var agora = DateTimeOffset.UtcNow;

        foreach (var recorrencia in recorrencias)
        {
            if (!recorrencia.VigenteEm(pedido.Ano, pedido.Mes)) { foraDaVez++; continue; }
            if (jaGeradas.Contains(recorrencia.Id)) { ignorados++; continue; }

            banco.Lancamentos.Add(new Lancamento
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                Natureza = recorrencia.Natureza,
                PessoaId = recorrencia.PessoaId,
                RecorrenciaId = recorrencia.Id,
                CompetenciaAno = pedido.Ano,
                CompetenciaMes = pedido.Mes,
                Descricao = recorrencia.Descricao,
                Valor = recorrencia.Valor,
                Vencimento = recorrencia.VencimentoEm(pedido.Ano, pedido.Mes),

                /* A classificação segue a da recorrência, menos o que foi inativado depois: inativo não recebe lançamento novo. */
                CategoriaId = recorrencia.Categoria is { Ativa: true } ? recorrencia.CategoriaId : null,
                CentroDeCustoId = recorrencia.CentroDeCusto is { Ativo: true } ? recorrencia.CentroDeCustoId : null,

                Situacao = SituacaoLancamento.Aberto,
                CriadoEm = agora,
                AtualizadoEm = agora,
            });

            gerados++;
        }

        try
        {
            await banco.SaveChangesAsync(cancelamento);
        }
        catch (DbUpdateException erro) when (erro.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        })
        {
            /* Outra geração da mesma competência passou entre a leitura e a gravação: o que se queria já está lá. */
            return Results.Ok(new ResultadoDasRecorrencias(0, gerados + ignorados, foraDaVez,
                "Os lançamentos desta competência já haviam sido gerados por outra operação."));
        }

        var recado = gerados switch
        {
            0 => "Nada a gerar: nenhum lançamento novo para esta competência.",
            1 => "1 lançamento gerado.",
            _ => $"{gerados} lançamentos gerados.",
        };

        return Results.Ok(new ResultadoDasRecorrencias(gerados, ignorados, foraDaVez, recado));
    }

    /* ------------------------------------------------------------- apoio */

    private static IQueryable<RecorrenciaNaLista> Resumir(IQueryable<Recorrencia> consulta) =>
        consulta.Select(recorrencia => new RecorrenciaNaLista(
            recorrencia.Id,
            recorrencia.Natureza,
            recorrencia.PessoaId,
            recorrencia.Pessoa!.Codigo,
            recorrencia.Pessoa!.Nome,
            recorrencia.Descricao,
            recorrencia.Valor,
            recorrencia.Frequencia,
            recorrencia.DiaDeVencimento,
            recorrencia.InicioEm,
            recorrencia.FimEm,
            recorrencia.Ativa,
            recorrencia.CategoriaId,
            recorrencia.Categoria!.Nome,
            recorrencia.CentroDeCustoId,
            recorrencia.CentroDeCusto!.Nome));

    private static void Aplicar(Recorrencia recorrencia, DadosDaRecorrencia dados, DateTimeOffset agora)
    {
        recorrencia.PessoaId = dados.PessoaId;
        recorrencia.Descricao = dados.Descricao!.Trim();
        recorrencia.Valor = decimal.Round(dados.Valor, 2, MidpointRounding.AwayFromZero);
        recorrencia.Frequencia = dados.Frequencia;
        recorrencia.DiaDeVencimento = dados.DiaDeVencimento;
        recorrencia.InicioEm = dados.InicioEm!.Value;
        recorrencia.FimEm = dados.FimEm;
        recorrencia.CategoriaId = dados.CategoriaId;
        recorrencia.CentroDeCustoId = dados.CentroDeCustoId;
        recorrencia.AtualizadoEm = agora;
    }

    /// <param name="natureza">A pedida, ao cadastrar; a gravada, ao alterar.</param>
    /// <param name="atual">A recorrência sendo alterada: a classificação dela passa mesmo inativa.</param>
    private static async Task<List<Problema>> Conferir(
        DadosDaRecorrencia dados,
        NaturezaLancamento natureza,
        Recorrencia? atual,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var problemas = new List<Problema>();
        var comNatureza = Enum.IsDefined(natureza);

        if (!comNatureza)
        {
            problemas.Add(new Problema("natureza", "Natureza não informada",
                "Não foi dito se a recorrência é a receber ou a pagar.",
                "Informe a natureza: Receber ou Pagar."));
        }
        else
        {
            /* A mesma regra do lançamento: a receber pede cliente, a pagar pede fornecedor. */
            var (papel, papelPorExtenso) = natureza == NaturezaLancamento.Pagar
                ? (Papel.Fornecedor, "fornecedor")
                : (Papel.Cliente, "cliente");

            var pessoa = await banco.Pessoas.AsNoTracking()
                .Include(item => item.Papeis)
                .FirstOrDefaultAsync(item => item.Id == dados.PessoaId, cancelamento);

            if (pessoa is null)
            {
                problemas.Add(new Problema("pessoaId", "Pessoa não encontrada",
                    "A pessoa informada não existe neste cadastro.",
                    "Escolha alguém da lista."));
            }
            else if (pessoa.Papeis.All(item => item.Papel != papel))
            {
                problemas.Add(new Problema("pessoaId", "Pessoa sem o papel",
                    $"“{pessoa.Nome}” não está marcada como {papelPorExtenso}.",
                    $"Abra o cadastro dela e marque o papel {papel}."));
            }
        }

        if (string.IsNullOrWhiteSpace(dados.Descricao))
        {
            problemas.Add(new Problema("descricao", FalhaNaValidacao,
                "A descrição não foi informada.",
                "Diga o que se repete: “Aluguel do escritório”, “Licença do sistema contábil”."));
        }
        else if (dados.Descricao.Trim().Length > 200)
        {
            problemas.Add(new Problema("descricao", FalhaNaValidacao,
                "A descrição passa de 200 caracteres.",
                "Resuma: os detalhes cabem no lançamento, depois de gerado."));
        }

        if (dados.Valor <= 0)
        {
            problemas.Add(new Problema("valor", FalhaNaValidacao,
                "O valor precisa ser maior que zero.",
                "Informe o valor de cada lançamento gerado, e não a soma do ano."));
        }

        if (!Enum.IsDefined(dados.Frequencia))
        {
            problemas.Add(new Problema("frequencia", FalhaNaValidacao,
                "A frequência não foi informada.",
                "Escolha mensal, trimestral, semestral ou anual."));
        }

        if (dados.DiaDeVencimento is < 1 or > 31)
        {
            problemas.Add(new Problema("diaDeVencimento", FalhaNaValidacao,
                $"O dia informado foi {dados.DiaDeVencimento}.",
                "Informe um dia entre 1 e 31. Nos meses mais curtos, o vencimento cai no último dia."));
        }

        if (dados.InicioEm is null)
        {
            problemas.Add(new Problema("inicioEm", FalhaNaValidacao,
                "O início não foi informado.",
                "Informe a partir de quando a recorrência vale: o mês dele é a primeira competência."));
        }
        else if (dados.FimEm is { } fim && fim < dados.InicioEm.Value)
        {
            problemas.Add(new Problema("fimEm", FalhaNaValidacao,
                "O fim é anterior ao início.",
                "Informe um fim depois do início, ou deixe em branco para não ter prazo."));
        }

        if (comNatureza)
        {
            problemas.AddRange(await Lancamentos.ConferirClassificacao(
                natureza, dados.CategoriaId, dados.CentroDeCustoId,
                atual?.CategoriaId, atual?.CentroDeCustoId, banco, cancelamento));
        }

        return problemas;
    }
}

/// <param name="CodigoDaPessoa">O código da pessoa, para a lista mostrar como nos lançamentos.</param>
/// <param name="InicioEm">A primeira competência é o mês desta data, e é dele que a frequência conta.</param>
/// <param name="FimEm">Nulo enquanto não tem prazo para acabar.</param>
public record RecorrenciaNaLista(
    Guid Id,
    NaturezaLancamento Natureza,
    Guid PessoaId,
    string CodigoDaPessoa,
    string NomeDaPessoa,
    string Descricao,
    decimal Valor,
    FrequenciaDeRecorrencia Frequencia,
    int DiaDeVencimento,
    DateOnly InicioEm,
    DateOnly? FimEm,
    bool Ativa,
    Guid? CategoriaId,
    string? Categoria,
    Guid? CentroDeCustoId,
    string? CentroDeCusto);

/// <param name="Natureza">Obrigatória ao cadastrar. Ignorada ao alterar: não muda depois de criada.</param>
/// <param name="Valor">O valor de cada lançamento gerado.</param>
/// <param name="DiaDeVencimento">De 1 a 31. Nos meses mais curtos, o último dia.</param>
/// <param name="InicioEm">Obrigatório. O mês dele é a primeira competência.</param>
/// <param name="FimEm">Opcional. Depois dele, a recorrência para de gerar.</param>
/// <param name="CategoriaId">Opcional. Da mesma natureza, e ativa.</param>
/// <param name="CentroDeCustoId">Opcional. Ativo.</param>
/// <param name="Ativa">Ignorado ao cadastrar: recorrência nasce ativa.</param>
public record DadosDaRecorrencia(
    NaturezaLancamento Natureza,
    Guid PessoaId,
    string? Descricao,
    decimal Valor,
    FrequenciaDeRecorrencia Frequencia,
    int DiaDeVencimento,
    DateOnly? InicioEm,
    DateOnly? FimEm = null,
    Guid? CategoriaId = null,
    Guid? CentroDeCustoId = null,
    bool Ativa = true);

public record PedidoDasRecorrencias(int Ano, int Mes);

/// <param name="Gerados">Lançamentos criados agora.</param>
/// <param name="Ignorados">Já existiam nesta competência.</param>
/// <param name="ForaDaVez">Inativas, fora da vigência, ou num mês que a frequência pula.</param>
public record ResultadoDasRecorrencias(int Gerados, int Ignorados, int ForaDaVez, string Recado);
