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
            .Produces<PaginaDeContratos>();

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

        grupo.MapGet("/{id:guid}/valores", Valores)
            .WithName("ValoresDoContrato")
            .WithSummary("O histórico de valores do contrato")
            .WithDescription("Do mais recente para o mais antigo, com a competência em que cada valor passou a valer e por quê.")
            .Produces<List<ValorNaLista>>()
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapPost("/reajustar", Reajustar)
            .WithName("ReajustarContratos")
            .WithSummary("Reajusta contratos por percentual, a partir de uma competência")
            .WithDescription("Abre uma vigência nova em cada contrato ativo escolhido, com o valor que ele tem hoje corrigido pelo percentual. Pode ser executado quantas vezes for preciso: contrato que já tem vigência nessa competência é ignorado, e não reajustado de novo.")
            .Produces<ResultadoDoReajuste>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity);

        return rotas;
    }

    /// <summary>
    /// Lista os contratos, paginados, com o total mensal recorrente.
    ///
    /// O <c>TotalMensalAtivo</c> é somado no banco, sobre todos os contratos
    /// ativos — não sobre a página. Esse número responde "quanto o escritório
    /// fatura por mês", e somá-lo da página daria uma resposta menor a cada
    /// cliente novo, sem ninguém notar.
    /// </summary>
    private static async Task<IResult> Listar(
        NexoDbContext banco,
        CancellationToken cancelamento,
        [FromQuery] string? busca = null,
        [FromQuery] SituacaoContrato? situacao = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 25,
        [FromQuery] OrdemDeContratos ordenarPor = OrdemDeContratos.Codigo,
        [FromQuery] Direcao direcao = Direcao.Crescente)
    {
        pagina = Math.Max(1, pagina);
        tamanho = Math.Clamp(tamanho, 1, 200);

        /* "Quanto este contrato vale" é sempre uma pergunta sobre uma competência, e aqui ela é a de hoje. */
        var competencia = CompetenciaDeHoje();

        IQueryable<Contrato> consulta = banco.Contratos.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim();

            /* Procura-se contrato pelo código, pelo cliente ou pelo que ele cobra. */
            consulta = consulta.Where(contrato =>
                EF.Functions.ILike(contrato.Codigo, $"%{termo}%")
                || EF.Functions.ILike(contrato.Descricao, $"%{termo}%")
                || EF.Functions.ILike(contrato.Pessoa!.Codigo, $"%{termo}%")
                || EF.Functions.ILike(contrato.Pessoa!.Nome, $"%{termo}%")
                || EF.Functions.ILike(contrato.Pessoa!.NomeFantasia, $"%{termo}%"));
        }

        if (situacao is { } filtro) consulta = consulta.Where(contrato => contrato.Situacao == filtro);

        var total = await consulta.CountAsync(cancelamento);

        /*
         * O total mensal é dos ativos, sempre — não da busca nem da página.
         * É o número que responde quanto entra por mês, e ele não muda porque
         * alguém digitou algo na busca.
         */
        var totalMensalAtivo = await banco.Contratos.AsNoTracking()
            .Where(contrato => contrato.Situacao == SituacaoContrato.Ativo)
            .Select(contrato => (decimal?)contrato.Valores
                .Where(valor => valor.VigenteDeAno * 12 + valor.VigenteDeMes <= competencia)
                .OrderByDescending(valor => valor.VigenteDeAno * 12 + valor.VigenteDeMes)
                .Select(valor => valor.Valor)
                .FirstOrDefault())
            .SumAsync(cancelamento) ?? 0m;

        var itens = await Ordenar(consulta, ordenarPor, direcao, competencia)
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .Select(contrato => new ContratoNaLista(
                contrato.Id,
                contrato.Codigo,
                contrato.PessoaId,
                contrato.Pessoa!.Codigo,
                contrato.Pessoa!.Nome,
                contrato.Descricao,
                contrato.Valores
                    .Where(valor => valor.VigenteDeAno * 12 + valor.VigenteDeMes <= competencia)
                    .OrderByDescending(valor => valor.VigenteDeAno * 12 + valor.VigenteDeMes)
                    .Select(valor => valor.Valor)
                    .FirstOrDefault(),
                contrato.DiaDeVencimento,
                contrato.Situacao))
            .ToListAsync(cancelamento);

        return Results.Ok(new PaginaDeContratos(itens, total, pagina, tamanho, totalMensalAtivo));
    }

    /// <summary>
    /// A ordem da listagem, decidida no banco — ver <see cref="Direcao"/>.
    ///
    /// <para>
    /// <b>Código ordena pelo comprimento antes do texto.</b> Diferente do de
    /// pessoa, este vem preenchido com zeros — <c>C0001</c> —, então até o
    /// milésimo nono centésimo nonagésimo nono a ordem de texto já é a
    /// numérica. Do <c>C10000</c> em diante ela deixa de ser, e o contrato mais
    /// novo do escritório apareceria no meio da lista. Comparar o tamanho antes
    /// resolve isso hoje, quando não custa nada, em vez de no dia em que
    /// custar.
    /// </para>
    /// <para>
    /// <b>Valor é a coluna em que o empate é a regra</b>, e não a exceção: meia
    /// dúzia de clientes na mesma faixa de honorário paga exatamente o mesmo.
    /// Por isso toda ordem daqui termina no identificador.
    /// </para>
    /// </summary>
    private static IOrderedQueryable<Contrato> Ordenar(
        IQueryable<Contrato> consulta,
        OrdemDeContratos por,
        Direcao direcao,
        int competencia)
    {
        var decrescente = direcao == Direcao.Decrescente;

        IOrderedQueryable<Contrato> ordenada = por switch
        {
            OrdemDeContratos.Cliente => decrescente
                ? consulta.OrderByDescending(contrato => contrato.Pessoa!.Nome)
                : consulta.OrderBy(contrato => contrato.Pessoa!.Nome),

            OrdemDeContratos.Valor => decrescente
                ? consulta.OrderByDescending(contrato => contrato.Valores
                    .Where(valor => valor.VigenteDeAno * 12 + valor.VigenteDeMes <= competencia)
                    .OrderByDescending(valor => valor.VigenteDeAno * 12 + valor.VigenteDeMes)
                    .Select(valor => valor.Valor)
                    .FirstOrDefault())
                : consulta.OrderBy(contrato => contrato.Valores
                    .Where(valor => valor.VigenteDeAno * 12 + valor.VigenteDeMes <= competencia)
                    .OrderByDescending(valor => valor.VigenteDeAno * 12 + valor.VigenteDeMes)
                    .Select(valor => valor.Valor)
                    .FirstOrDefault()),

            OrdemDeContratos.Vencimento => decrescente
                ? consulta.OrderByDescending(contrato => contrato.DiaDeVencimento)
                : consulta.OrderBy(contrato => contrato.DiaDeVencimento),

            _ => decrescente
                ? consulta.OrderByDescending(contrato => contrato.Codigo.Length)
                    .ThenByDescending(contrato => contrato.Codigo)
                : consulta.OrderBy(contrato => contrato.Codigo.Length)
                    .ThenBy(contrato => contrato.Codigo),
        };

        return ordenada.ThenBy(contrato => contrato.Id);
    }

    private static async Task<IResult> Obter(Guid id, NexoDbContext banco, CancellationToken cancelamento)
    {
        var competencia = CompetenciaDeHoje();

        var contrato = await banco.Contratos.AsNoTracking()
            .Where(contrato => contrato.Id == id)
            .Select(contrato => new ContratoDetalhado(
                contrato.Id,
                contrato.Codigo,
                contrato.PessoaId,
                contrato.Descricao,
                contrato.Valores
                    .Where(valor => valor.VigenteDeAno * 12 + valor.VigenteDeMes <= competencia)
                    .OrderByDescending(valor => valor.VigenteDeAno * 12 + valor.VigenteDeMes)
                    .Select(valor => valor.Valor)
                    .FirstOrDefault(),
                contrato.DiaDeVencimento,
                contrato.InicioDaVigencia,
                contrato.FimDaVigencia,
                contrato.Situacao,
                contrato.Observacoes))
            .FirstOrDefaultAsync(cancelamento);

        return contrato is null ? Results.NotFound() : Results.Ok(contrato);
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

        /* O valor nasce com vigência: a competência do início, que é a primeira mensalidade possível. */
        banco.ValoresDeContrato.Add(new ValorDoContrato
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ContratoId = contrato.Id,
            Valor = dados.Valor,
            VigenteDeAno = contrato.InicioDaVigencia.Year,
            VigenteDeMes = contrato.InicioDaVigencia.Month,
            Motivo = ValorInicial,
            CriadoEm = DateTimeOffset.UtcNow,
        });

        await banco.SaveChangesAsync(cancelamento);

        return Results.Created($"/contratos/{contrato.Id}", Detalhar(contrato, dados.Valor));
    }

    private static async Task<IResult> Alterar(
        Guid id,
        [FromBody] DadosDeContrato dados,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var contrato = await banco.Contratos
            .Include(c => c.Valores)
            .FirstOrDefaultAsync(c => c.Id == id, cancelamento);

        if (contrato is null) return Results.NotFound();

        var problemas = await Conferir(dados, banco, cancelamento);
        if (problemas.Count > 0)
            return Results.Json(new RespostaComProblemas(problemas), statusCode: 422);

        Aplicar(dados, contrato);
        contrato.AtualizadoEm = DateTimeOffset.UtcNow;

        /*
         * Alterar o valor aqui é corrigir, e não reajustar: vale desde o começo da
         * vigência em curso, e não abre vigência nova. Quem quer mudar a partir de
         * um mês usa o reajuste, que deixa o valor de antes no histórico.
         */
        Corrigir(contrato, dados.Valor);

        await banco.SaveChangesAsync(cancelamento);

        return Results.Ok(Detalhar(contrato, dados.Valor));
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
            .Include(contrato => contrato.Valores)
            .Where(contrato => pedido.ContratoIds == null || pedido.ContratoIds.Contains(contrato.Id))
            .ToListAsync(cancelamento);

        /*
         * Cancelados ficam de fora da conta, do mesmo jeito que ficam de fora
         * do índice único. É o que permite corrigir: cancela a mensalidade
         * errada e gera de novo, com o valor certo, na mesma competência.
         */
        var jaGerados = await banco.Lancamentos.AsNoTracking()
            .Where(lancamento =>
                lancamento.CompetenciaAno == pedido.Ano
                && lancamento.CompetenciaMes == pedido.Mes
                && lancamento.ContratoId != null
                && lancamento.Situacao != SituacaoLancamento.Cancelado)
            .Select(lancamento => lancamento.ContratoId!.Value)
            .ToListAsync(cancelamento);

        var geradas = 0;
        var ignoradas = 0;
        var foraDeVigencia = 0;

        foreach (var contrato in candidatos)
        {
            if (!contrato.VigenteEm(pedido.Ano, pedido.Mes)) { foraDeVigencia++; continue; }
            if (jaGerados.Contains(contrato.Id)) { ignoradas++; continue; }

            /* O valor é o que valia na competência gerada: reajuste a partir de abril não mexe em março. */
            if (contrato.ValorEm(pedido.Ano, pedido.Mes) is not { } valorDaCompetencia)
            {
                foraDeVigencia++;
                continue;
            }

            banco.Lancamentos.Add(new Lancamento
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                Natureza = NaturezaLancamento.Receber,
                PessoaId = contrato.PessoaId,
                ContratoId = contrato.Id,
                CompetenciaAno = pedido.Ano,
                CompetenciaMes = pedido.Mes,
                Descricao = contrato.Descricao,
                Valor = valorDaCompetencia,
                Vencimento = contrato.VencimentoEm(pedido.Ano, pedido.Mes),
                Situacao = SituacaoLancamento.Aberto,
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

    private static async Task<IResult> Valores(Guid id, NexoDbContext banco, CancellationToken cancelamento)
    {
        if (!await banco.Contratos.AnyAsync(contrato => contrato.Id == id, cancelamento))
            return Results.NotFound();

        var valores = await banco.ValoresDeContrato.AsNoTracking()
            .Where(valor => valor.ContratoId == id)
            .OrderByDescending(valor => valor.VigenteDeAno * 12 + valor.VigenteDeMes)
            .Select(valor => new ValorNaLista(
                valor.Id, valor.Valor, valor.VigenteDeAno, valor.VigenteDeMes,
                valor.Motivo, valor.Percentual, valor.CriadoEm))
            .ToListAsync(cancelamento);

        return Results.Ok(valores);
    }

    /// <summary>
    /// Reajusta contratos por percentual, a partir de uma competência.
    ///
    /// <para>
    /// <b>Percentual, e não valor digitado.</b> Reajuste de carteira vem de um
    /// índice, e cada contrato tem o próprio valor: digitar dezenas de valores
    /// novos à mão é onde o erro mora. Cada um recebe o que tem hoje corrigido
    /// pelo percentual.
    /// </para>
    /// <para>
    /// <b>Rodar duas vezes não compõe o percentual.</b> Quem recusa a segunda
    /// vigência da mesma competência é o índice único do banco, do mesmo jeito que
    /// a mensalidade não se gera duas vezes. A conferência aqui serve para dar uma
    /// resposta boa, e não para garantir o dado.
    /// </para>
    /// </summary>
    private static async Task<IResult> Reajustar(
        [FromBody] PedidoDeReajuste pedido,
        NexoDbContext banco,
        IContextoDeTenant contexto,
        CancellationToken cancelamento)
    {
        if (contexto.TenantAtual is not { } tenant) return Results.Unauthorized();

        var problemas = new List<Problema>();

        if (pedido.Mes is < 1 or > 12)
        {
            problemas.Add(new Problema("mes", "Competência inválida",
                $"O mês informado foi {pedido.Mes}.",
                "Informe um mês entre 1 e 12."));
        }

        if (pedido.Ano is < 2000 or > 2100)
        {
            problemas.Add(new Problema("ano", "Competência inválida",
                $"O ano informado foi {pedido.Ano}.",
                "Informe um ano entre 2000 e 2100."));
        }

        if (pedido.Percentual == 0m)
        {
            problemas.Add(new Problema("percentual", "Reajuste sem percentual",
                "O percentual informado foi zero.",
                "Informe quanto o valor muda: 4,5 sobe 4,5%."));
        }
        else if (pedido.Percentual is < -99m or > 1000m)
        {
            problemas.Add(new Problema("percentual", "Percentual fora do razoável",
                $"O percentual informado foi {pedido.Percentual}.",
                "Informe um percentual entre -99 e 1000. Acima disso costuma ser vírgula no lugar errado."));
        }

        if (string.IsNullOrWhiteSpace(pedido.Motivo))
        {
            problemas.Add(new Problema("motivo", "Reajuste sem motivo",
                "O motivo não foi informado.",
                "Escreva de onde veio o índice: “IPCA de 2026”, “reajuste anual combinado”."));
        }

        if (problemas.Count > 0)
            return Results.Json(new RespostaComProblemas(problemas), statusCode: 422);

        var escolhidos = await banco.Contratos
            .Include(contrato => contrato.Valores)
            .Where(contrato => pedido.ContratoIds == null || pedido.ContratoIds.Contains(contrato.Id))
            .ToListAsync(cancelamento);

        var competencia = ValorDoContrato.Competencia(pedido.Ano, pedido.Mes);
        var motivo = pedido.Motivo!.Trim();
        var agora = DateTimeOffset.UtcNow;

        var reajustados = 0;
        var ignorados = 0;
        var foraDeVigencia = 0;

        foreach (var contrato in escolhidos)
        {
            /* Só o ativo: suspenso e encerrado não geram mensalidade, e reajustá-los seria mexer no que está parado. */
            if (contrato.Situacao != SituacaoContrato.Ativo) { foraDeVigencia++; continue; }

            if (contrato.Valores.Any(valor =>
                    ValorDoContrato.Competencia(valor.VigenteDeAno, valor.VigenteDeMes) == competencia))
            {
                ignorados++;
                continue;
            }

            if (contrato.ValorEm(pedido.Ano, pedido.Mes) is not { } valorDeAgora) { foraDeVigencia++; continue; }

            var reajustado = decimal.Round(
                valorDeAgora * (1m + pedido.Percentual / 100m), 2, MidpointRounding.AwayFromZero);

            if (reajustado <= 0m) { foraDeVigencia++; continue; }

            banco.ValoresDeContrato.Add(new ValorDoContrato
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                ContratoId = contrato.Id,
                Valor = reajustado,
                VigenteDeAno = pedido.Ano,
                VigenteDeMes = pedido.Mes,
                Motivo = motivo,
                Percentual = pedido.Percentual,
                CriadoEm = agora,
            });

            reajustados++;
        }

        try
        {
            await banco.SaveChangesAsync(cancelamento);
        }
        catch (DbUpdateException erro) when (erro.InnerException is PostgresException { SqlState: "23505" })
        {
            /* Outro reajuste da mesma competência passou entre a leitura e a gravação: o valor novo já está lá. */
            return Results.Ok(new ResultadoDoReajuste(0, reajustados + ignorados, foraDeVigencia,
                "Estes contratos já haviam sido reajustados nesta competência."));
        }

        var recado = reajustados switch
        {
            0 => "Nada a reajustar: nenhum contrato novo para esta competência.",
            1 => "1 contrato reajustado.",
            _ => $"{reajustados} contratos reajustados.",
        };

        return Results.Ok(new ResultadoDoReajuste(reajustados, ignorados, foraDeVigencia, recado));
    }

    /* ------------------------------------------------------------- apoio */

    /// <summary>O motivo da vigência que nasce com o contrato.</summary>
    private const string ValorInicial = "Valor inicial";

    /// <summary>A competência de hoje, que é a que responde "quanto este contrato vale".</summary>
    private static int CompetenciaDeHoje()
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        return ValorDoContrato.Competencia(hoje.Year, hoje.Month);
    }

    /// <summary>
    /// Corrige o valor da vigência em curso, sem abrir outra, e mantém a primeira
    /// começando junto com a vigência do contrato: mudar o início leva o valor
    /// inicial junto, senão sobraria uma vigência começando antes do contrato.
    /// </summary>
    private static void Corrigir(Contrato contrato, decimal valor)
    {
        var ordenadas = contrato.Valores
            .OrderBy(item => ValorDoContrato.Competencia(item.VigenteDeAno, item.VigenteDeMes))
            .ToList();

        if (ordenadas.Count == 0) return;

        var primeira = ordenadas[0];
        primeira.VigenteDeAno = contrato.InicioDaVigencia.Year;
        primeira.VigenteDeMes = contrato.InicioDaVigencia.Month;

        var competencia = CompetenciaDeHoje();

        var emCurso = ordenadas.LastOrDefault(item =>
            ValorDoContrato.Competencia(item.VigenteDeAno, item.VigenteDeMes) <= competencia) ?? primeira;

        emCurso.Valor = valor;
    }

    private static async Task<List<Problema>> Conferir(
        DadosDeContrato dados,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var problemas = new List<Problema>();

        /*
         * Não basta a pessoa existir: ela precisa carregar o papel de cliente.
         * Sem essa conferência, um fornecedor ou um colaborador entraria num
         * contrato por engano de escolha na lista, e o erro só apareceria na
         * hora de cobrar.
         */
        var pessoa = await banco.Pessoas.AsNoTracking()
            .Include(p => p.Papeis)
            .FirstOrDefaultAsync(p => p.Id == dados.PessoaId, cancelamento);

        if (pessoa is null)
        {
            problemas.Add(new Problema("pessoaId", "Contrato sem cliente",
                "A pessoa informada não existe neste cadastro.",
                "Escolha alguém da lista."));
        }
        else if (pessoa.Papeis.All(p => p.Papel != Papel.Cliente))
        {
            problemas.Add(new Problema("pessoaId", "Contrato sem cliente",
                $"“{pessoa.Nome}” não está marcada como cliente.",
                "Abra o cadastro dela e marque o papel Cliente."));
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
        contrato.PessoaId = dados.PessoaId;
        contrato.Descricao = (dados.Descricao ?? string.Empty).Trim();
        contrato.DiaDeVencimento = dados.DiaDeVencimento;
        contrato.InicioDaVigencia = dados.InicioDaVigencia;
        contrato.FimDaVigencia = dados.FimDaVigencia;
        contrato.Situacao = dados.Situacao;
        contrato.Observacoes = (dados.Observacoes ?? string.Empty).Trim();
    }

    private static ContratoDetalhado Detalhar(Contrato contrato, decimal valor) => new(
        contrato.Id,
        contrato.Codigo,
        contrato.PessoaId,
        contrato.Descricao,
        valor,
        contrato.DiaDeVencimento,
        contrato.InicioDaVigencia,
        contrato.FimDaVigencia,
        contrato.Situacao,
        contrato.Observacoes);
}

public record DadosDeContrato(
    Guid PessoaId,
    string? Descricao,
    decimal Valor,
    int DiaDeVencimento,
    DateOnly InicioDaVigencia,
    DateOnly? FimDaVigencia,
    SituacaoContrato Situacao,
    string? Observacoes);

/// <summary>Por qual coluna a listagem de contratos é ordenada.</summary>
public enum OrdemDeContratos
{
    Codigo = 1,
    Cliente = 2,
    Valor = 3,
    Vencimento = 4,
}

/// <param name="Total">Quantos contratos a seleção tem, e não quantos vieram nesta página.</param>
/// <param name="TotalMensalAtivo">Soma dos contratos ativos, independente de busca e de página.</param>
public record PaginaDeContratos(
    List<ContratoNaLista> Itens,
    int Total,
    int Pagina,
    int Tamanho,
    decimal TotalMensalAtivo);

public record ContratoNaLista(
    Guid Id,
    string Codigo,
    Guid PessoaId,
    string CodigoDaPessoa,
    string NomeDaPessoa,
    string Descricao,
    decimal Valor,
    int DiaDeVencimento,
    SituacaoContrato Situacao);

public record ContratoDetalhado(
    Guid Id,
    string Codigo,
    Guid PessoaId,
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

/// <param name="Percentual">Em pontos percentuais: 4,5 sobe 4,5%. Negativo reduz.</param>
/// <param name="Motivo">De onde veio o índice. Fica no histórico de cada contrato.</param>
/// <param name="ContratoIds">Nulo para reajustar todos os contratos ativos.</param>
public record PedidoDeReajuste(decimal Percentual, int Ano, int Mes, string? Motivo, List<Guid>? ContratoIds);

/// <param name="Reajustados">Contratos que ganharam vigência nova.</param>
/// <param name="Ignorados">Já tinham vigência nesta competência.</param>
/// <param name="ForaDeVigencia">Suspensos, encerrados, ou sem valor nessa competência.</param>
public record ResultadoDoReajuste(int Reajustados, int Ignorados, int ForaDeVigencia, string Recado);

/// <param name="VigenteDeAno">A competência a partir da qual este valor vale.</param>
/// <param name="Percentual">O percentual do reajuste que o criou. Nulo quando o valor foi digitado.</param>
public record ValorNaLista(
    Guid Id,
    decimal Valor,
    int VigenteDeAno,
    int VigenteDeMes,
    string Motivo,
    decimal? Percentual,
    DateTimeOffset CriadoEm);
