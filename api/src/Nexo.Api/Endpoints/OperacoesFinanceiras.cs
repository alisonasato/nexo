using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexo.Api.Cobranca;
using Nexo.Api.Dados;
using Nexo.Api.Dominio;
using Npgsql;

namespace Nexo.Api.Endpoints;

/// <summary>
/// As operações avançadas sobre os lançamentos, a receber e a pagar: lançar
/// em parcelas, renegociar e baixar em lote.
///
/// <para>
/// Segundo PR da fase 1 do módulo financeiro, descrito no FINANCEIRO.md. Tudo
/// aqui se apoia no primeiro: a situação do lançamento como trava de
/// concorrência, e a retirada da cobrança do PSP antes de o título deixar de
/// estar em aberto.
/// </para>
/// </summary>
public static class OperacoesFinanceiras
{
    /// <summary>
    /// O teto da baixa em lote. Cada item pode precisar tirar uma cobrança do
    /// PSP, uma chamada de rede de cada vez; cem é o que cabe numa requisição
    /// sem que quem clicou fique olhando a tela sem saber se ela travou.
    /// </summary>
    private const int MaximoPorLote = 100;

    private static readonly CultureInfo Real = CultureInfo.GetCultureInfo("pt-BR");

    public static IEndpointRouteBuilder MapOperacoesFinanceiras(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/lancamentos").WithTags("Lançamentos");

        grupo.MapPost("/parcelamentos", Parcelar)
            .WithName("ParcelarLancamento")
            .WithSummary("Lança um valor dividido em parcelas mensais")
            .WithDescription("Os centavos que sobram da divisão vão para a primeira parcela, e cada vencimento parte da primeira data. Com uma parcela só, é um lançamento avulso comum.")
            .Produces<ParcelamentoCriado>(StatusCodes.Status201Created)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPost("/{id:guid}/renegociar", Renegociar)
            .WithName("RenegociarLancamento")
            .WithSummary("Troca um título em aberto por parcelas novas")
            .WithDescription("O título original vira renegociado e continua ocupando a competência; as parcelas novas apontam para ele. Juros e multa acrescentam, desconto abate.")
            .Produces<RenegociacaoCriada>(StatusCodes.Status201Created)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapPost("/baixar-em-lote", BaixarEmLote)
            .WithName("BaixarLancamentosEmLote")
            .WithSummary("Registra a baixa de vários lançamentos de uma vez")
            .WithDescription("Cada lançamento é baixado ou recusado por conta própria: um já pago não impede os outros. O valor baixado é o valor do lançamento.")
            .Produces<ResultadoDaBaixaEmLote>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity);

        return rotas;
    }

    /* -------------------------------------------------------- parcelar */

    private static async Task<IResult> Parcelar(
        [FromBody] DadosDoParcelamento dados,
        NexoDbContext banco,
        IContextoDeTenant contexto,
        CancellationToken cancelamento)
    {
        if (contexto.TenantAtual is not { } tenant) return Results.Unauthorized();

        var total = decimal.Round(dados.ValorTotal, 2, MidpointRounding.AwayFromZero);

        /* A mesma conferência do lançamento avulso: natureza, a pessoa com o
           papel que ela pede, descrição, valor e competência. Parcelar não
           afrouxa nada. */
        var problemas = await Lancamentos.Conferir(
            new DadosDoAvulso(dados.Natureza, dados.PessoaId, dados.Descricao, total, dados.PrimeiroVencimento,
                dados.CompetenciaAno, dados.CompetenciaMes, dados.CategoriaId, dados.CentroDeCustoId),
            banco, cancelamento);

        problemas.AddRange(ConferirParcelas(total, dados.Parcelas));

        if (problemas.Count > 0)
            return Results.Json(new RespostaComProblemas(problemas), statusCode: 422);

        var pessoa = await banco.Pessoas.FirstAsync(p => p.Id == dados.PessoaId, cancelamento);
        var agora = DateTimeOffset.UtcNow;

        /* Carregados para a resposta já trazer os nomes; a conferência acima garantiu que existem. */
        var categoria = dados.CategoriaId is { } idDaCategoria
            ? await banco.Categorias.FirstAsync(item => item.Id == idDaCategoria, cancelamento)
            : null;
        var centro = dados.CentroDeCustoId is { } idDoCentro
            ? await banco.CentrosDeCusto.FirstAsync(item => item.Id == idDoCentro, cancelamento)
            : null;

        /* Com uma parcela só não há grupo: "1 de 1" é ruído na tela. */
        var emGrupo = dados.Parcelas > 1;
        var grupo = Guid.NewGuid();

        var novos = Parcelas.Dividir(total, dados.Parcelas, dados.PrimeiroVencimento)
            .Select(parcela => new Lancamento
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                Natureza = dados.Natureza,
                PessoaId = pessoa.Id,
                Pessoa = pessoa,
                ContratoId = null,
                CompetenciaAno = dados.CompetenciaAno,
                CompetenciaMes = dados.CompetenciaMes,
                Descricao = dados.Descricao.Trim(),
                Valor = parcela.Valor,
                Vencimento = parcela.Vencimento,
                Situacao = SituacaoLancamento.Aberto,
                ParcelamentoId = emGrupo ? grupo : null,
                ParcelaNumero = emGrupo ? parcela.Numero : null,
                ParcelasTotal = emGrupo ? dados.Parcelas : null,
                CategoriaId = categoria?.Id,
                Categoria = categoria,
                CentroDeCustoId = centro?.Id,
                CentroDeCusto = centro,
                CriadoEm = agora,
                AtualizadoEm = agora,
            })
            .ToList();

        banco.Lancamentos.AddRange(novos);
        await banco.SaveChangesAsync(cancelamento);

        return Results.Json(
            new ParcelamentoCriado(emGrupo ? grupo : null, novos.Select(item => Lancamentos.Detalhar(item)).ToList()),
            statusCode: StatusCodes.Status201Created);
    }

    /* ------------------------------------------------------ renegociar */

    /// <summary>
    /// Troca um título em aberto por parcelas novas.
    ///
    /// <para>
    /// <b>A ordem importa.</b> Primeiro tudo o que pode recusar sem efeito
    /// nenhum: situação, motivo, valores. Depois a cobrança do PSP sai do ar. Só
    /// então o título muda e as parcelas nascem, numa gravação só. Se a gravação
    /// falhar depois de a cobrança sair do ar, o título continua em aberto e uma
    /// nova tentativa pede a exclusão de novo, que o PSP responde como não
    /// encontrada e conta como sucesso.
    /// </para>
    /// </summary>
    private static async Task<IResult> Renegociar(
        Guid id,
        [FromBody] DadosDaRenegociacao dados,
        NexoDbContext banco,
        ClienteDoAsaas asaas,
        IOptions<OpcoesDoAsaas> configuracao,
        ILoggerFactory registros,
        CancellationToken cancelamento)
    {
        var original = await banco.Lancamentos
            .Include(r => r.Pessoa)
            .Include(r => r.Categoria)
            .Include(r => r.CentroDeCusto)
            .FirstOrDefaultAsync(r => r.Id == id, cancelamento);

        if (original is null) return Results.NotFound();

        switch (original.Situacao)
        {
            case SituacaoLancamento.Pago:
                return Problema("id", "Lançamento já baixado",
                    $"Este lançamento foi baixado em {original.PagoEm:dd/MM/yyyy}.",
                    "Título pago não se renegocia. Se a baixa estava errada, estorne antes.");

            case SituacaoLancamento.Cancelado:
                return Problema("id", "Lançamento cancelado",
                    "Um lançamento cancelado não pode ser renegociado.",
                    "Se o valor voltou a ser devido, lance uma cobrança nova.");

            case SituacaoLancamento.Renegociado:
                return Problema("id", "Lançamento já renegociado",
                    "Este título já foi substituído por parcelas novas.",
                    "Se for o caso, renegocie as parcelas novas.");
        }

        var problemas = new List<Problema>();
        var motivo = (dados.Motivo ?? string.Empty).Trim();

        if (motivo.Length == 0)
        {
            problemas.Add(new Problema("motivo", "Motivo não informado",
                "Renegociar troca um título por outros, e quem olhar depois vai perguntar por quê.",
                "Escreva o motivo: “cliente pediu mais prazo”, “acordo de parcelamento”."));
        }

        foreach (var (campo, valor) in new[] { ("juros", dados.Juros), ("multa", dados.Multa), ("desconto", dados.Desconto) })
        {
            if (valor < 0)
            {
                problemas.Add(new Problema(campo, "Valor inválido",
                    "Juros, multa e desconto não podem ser negativos.",
                    "Use o desconto para abater, e juros e multa para acrescentar."));
            }
        }

        var novoTotal = decimal.Round(
            original.Valor + dados.Juros + dados.Multa - dados.Desconto, 2, MidpointRounding.AwayFromZero);

        if (novoTotal <= 0)
        {
            problemas.Add(new Problema("desconto", "Desconto maior que o título",
                $"Com este desconto o total ficaria em R$ {novoTotal.ToString("N2", Real)}.",
                "Para perdoar a dívida inteira, cancele o título e escreva o motivo."));
        }
        else
        {
            problemas.AddRange(ConferirParcelas(novoTotal, dados.Parcelas));
        }

        if (problemas.Count > 0)
            return Results.Json(new RespostaComProblemas(problemas), statusCode: 422);

        /* O título deixa de estar em aberto: a cobrança dele sai do ar antes. */
        var naoRetirou = await Cobrancas.RetirarDoPsp(
            original, asaas, configuracao.Value, registros.CreateLogger("Cobranca"), cancelamento);
        if (naoRetirou is not null) return naoRetirou;

        var agora = DateTimeOffset.UtcNow;

        original.Situacao = SituacaoLancamento.Renegociado;
        original.AtualizadoEm = agora;

        var renegociacao = new Renegociacao
        {
            Id = Guid.NewGuid(),
            TenantId = original.TenantId,
            OrigemId = original.Id,
            Juros = dados.Juros,
            Multa = dados.Multa,
            Desconto = dados.Desconto,
            Motivo = motivo,
            CriadoEm = agora,
        };

        var emGrupo = dados.Parcelas > 1;
        var grupo = Guid.NewGuid();

        /*
         * As parcelas novas não levam o contrato. Com ele, duas parcelas da
         * mesma competência colidiriam no índice único de mensalidade. O vínculo
         * com o contrato continua alcançável pelo título original, para onde
         * cada parcela aponta.
         */
        var parcelas = Parcelas.Dividir(novoTotal, dados.Parcelas, dados.PrimeiroVencimento)
            .Select(parcela => new Lancamento
            {
                Id = Guid.NewGuid(),
                TenantId = original.TenantId,
                Natureza = original.Natureza,
                PessoaId = original.PessoaId,
                Pessoa = original.Pessoa,
                ContratoId = null,
                CompetenciaAno = original.CompetenciaAno,
                CompetenciaMes = original.CompetenciaMes,
                Descricao = original.Descricao,
                Valor = parcela.Valor,
                Vencimento = parcela.Vencimento,
                Situacao = SituacaoLancamento.Aberto,
                ParcelamentoId = emGrupo ? grupo : null,
                ParcelaNumero = emGrupo ? parcela.Numero : null,
                ParcelasTotal = emGrupo ? dados.Parcelas : null,
                RenegociadoDeId = original.Id,

                /* O acordo muda prazo e valor, e não o que o dinheiro é: a classificação segue a do título. */
                CategoriaId = original.CategoriaId,
                Categoria = original.Categoria,
                CentroDeCustoId = original.CentroDeCustoId,
                CentroDeCusto = original.CentroDeCusto,
                CriadoEm = agora,
                AtualizadoEm = agora,
            })
            .ToList();

        banco.Renegociacoes.Add(renegociacao);
        banco.Lancamentos.AddRange(parcelas);

        try
        {
            await banco.SaveChangesAsync(cancelamento);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problema("situacao", "O lançamento mudou enquanto isto era feito",
                "Outra operação alterou este lançamento no mesmo instante, e nada foi renegociado.",
                "Recarregue a lista e confira a situação antes de tentar de novo.");
        }
        catch (DbUpdateException erro) when (erro.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        })
        {
            return Problema("id", "Lançamento já renegociado",
                "Este título foi renegociado por outra operação no mesmo instante.",
                "Recarregue a lista: as parcelas novas já estão lá.");
        }

        return Results.Json(
            new RenegociacaoCriada(renegociacao.Id, original.Id, parcelas.Select(item => Lancamentos.Detalhar(item)).ToList()),
            statusCode: StatusCodes.Status201Created);
    }

    /* -------------------------------------------------- baixar em lote */

    /// <summary>
    /// Baixa vários lançamentos de uma vez, pelo valor cobrado.
    ///
    /// <para>
    /// <b>Cada item por conta própria, e não tudo numa transação.</b> Numa
    /// seleção de vinte, um pode ter sido pago pelo PSP há um minuto. Recusar os
    /// vinte por causa dele obrigaria a desmarcar e tentar de novo, e a retirada
    /// das cobranças que já saíram do ar não se desfaz com rollback nenhum. A
    /// resposta diz quantos foram baixados e, para cada recusado, por quê.
    /// </para>
    /// </summary>
    private static async Task<IResult> BaixarEmLote(
        [FromBody] DadosDaBaixaEmLote dados,
        NexoDbContext banco,
        ClienteDoAsaas asaas,
        IOptions<OpcoesDoAsaas> configuracao,
        ILoggerFactory registros,
        CancellationToken cancelamento)
    {
        var ids = (dados.Ids ?? []).Distinct().ToList();

        if (ids.Count == 0)
        {
            return Problema("ids", "Nada selecionado",
                "Nenhum lançamento foi informado.",
                "Marque na lista os lançamentos a baixar.");
        }

        if (ids.Count > MaximoPorLote)
        {
            return Problema("ids", "Seleção grande demais",
                $"Foram informados {ids.Count} lançamentos.",
                $"Baixe no máximo {MaximoPorLote} de cada vez.");
        }

        var pagoEm = dados.PagoEm ?? DateOnly.FromDateTime(DateTime.Today);

        /* Uma conta para o lote inteiro, conferida uma vez: se ela não serve, nenhum item serve. */
        var (contaDaBaixa, semConta) = await ContasBancarias.ConferirContaDoMovimento(dados.ContaId, pagoEm, "contaId", "pagoEm", banco, cancelamento);
        if (semConta is not null) return Results.Json(new RespostaComProblemas([semConta]), statusCode: 422);

        var registro = registros.CreateLogger("Cobranca");
        var recusados = new List<RecusaNaBaixaEmLote>();
        var baixados = 0;

        foreach (var id in ids)
        {
            /* Um item recusado não pode deixar alterações pendentes para o
               próximo gravar junto. */
            banco.ChangeTracker.Clear();

            var lancamento = await banco.Lancamentos.FirstOrDefaultAsync(r => r.Id == id, cancelamento);

            if (lancamento is null)
            {
                recusados.Add(new RecusaNaBaixaEmLote(id, "Não encontrado."));
                continue;
            }

            if (lancamento.Situacao != SituacaoLancamento.Aberto)
            {
                recusados.Add(new RecusaNaBaixaEmLote(id, lancamento.Situacao switch
                {
                    SituacaoLancamento.Pago => $"Já baixado em {lancamento.PagoEm:dd/MM/yyyy}.",
                    SituacaoLancamento.Cancelado => "Cancelado.",
                    SituacaoLancamento.Renegociado => "Renegociado: baixe as parcelas novas.",
                    _ => "Não está em aberto.",
                }));
                continue;
            }

            if (await Cobrancas.TentarRetirarDoPsp(lancamento, asaas, configuracao.Value, registro, cancelamento)
                is { } falha)
            {
                recusados.Add(new RecusaNaBaixaEmLote(id, $"{falha.Titulo}: {falha.Descricao}"));
                continue;
            }

            lancamento.Situacao = SituacaoLancamento.Pago;
            lancamento.ValorPago = lancamento.Valor;
            lancamento.PagoEm = pagoEm;
            lancamento.OrigemDaBaixa = OrigensDeBaixa.Lote;
            lancamento.AtualizadoEm = DateTimeOffset.UtcNow;

            banco.MovimentosDeConta.Add(MovimentoDeConta.DaBaixa(lancamento, contaDaBaixa!.Id, lancamento.Valor, pagoEm));

            try
            {
                await banco.SaveChangesAsync(cancelamento);
                baixados++;
            }
            catch (DbUpdateConcurrencyException)
            {
                recusados.Add(new RecusaNaBaixaEmLote(id,
                    "Mudou enquanto a baixa era feita. Recarregue e confira a situação."));
            }
        }

        banco.ChangeTracker.Clear();

        return Results.Ok(new ResultadoDaBaixaEmLote(baixados, recusados));
    }

    /* ------------------------------------------------------------- apoio */

    private static List<Problema> ConferirParcelas(decimal total, int quantidade)
    {
        var problemas = new List<Problema>();

        if (quantidade is < 1 or > Parcelas.Maximo)
        {
            problemas.Add(new Problema("parcelas", "Parcelamento inválido",
                $"Foram pedidas {quantidade} parcelas.",
                $"Informe de 1 a {Parcelas.Maximo} parcelas."));
        }
        else if (total > 0 && total * 100m < quantidade)
        {
            problemas.Add(new Problema("parcelas", "Parcelamento inválido",
                "O valor não chega a um centavo por parcela.",
                "Diminua o número de parcelas."));
        }

        return problemas;
    }

    private static IResult Problema(string campo, string titulo, string descricao, string sugestao) =>
        Results.Json(
            new RespostaComProblemas([new Problema(campo, titulo, descricao, sugestao)]),
            statusCode: 422);
}

/// <param name="Natureza">Obrigatória, e a mesma para todas as parcelas.</param>
/// <param name="ValorTotal">O valor inteiro, antes de dividir.</param>
/// <param name="CompetenciaAno">O mês do serviço, igual para todas as parcelas.</param>
public record DadosDoParcelamento(
    NaturezaLancamento Natureza,
    Guid PessoaId,
    string Descricao,
    decimal ValorTotal,
    int Parcelas,
    DateOnly PrimeiroVencimento,
    int CompetenciaAno,
    int CompetenciaMes,
    Guid? CategoriaId = null,
    Guid? CentroDeCustoId = null);

/// <param name="ParcelamentoId">Nulo quando foi lançada uma parcela só.</param>
public record ParcelamentoCriado(Guid? ParcelamentoId, List<LancamentoNaLista> Parcelas);

/// <param name="Juros">Valor em reais acrescentado, e não taxa.</param>
/// <param name="Multa">Valor em reais acrescentado, e não taxa.</param>
/// <param name="Desconto">Valor em reais abatido.</param>
public record DadosDaRenegociacao(
    int Parcelas,
    DateOnly PrimeiroVencimento,
    decimal Juros,
    decimal Multa,
    decimal Desconto,
    string? Motivo);

public record RenegociacaoCriada(Guid RenegociacaoId, Guid OrigemId, List<LancamentoNaLista> Parcelas);

/// <param name="ContaId">A conta de todos os itens: um lote é a conferência de um extrato só.</param>
/// <param name="PagoEm">Quando o dinheiro entrou ou saiu. Vazio é hoje.</param>
public record DadosDaBaixaEmLote(Guid ContaId, List<Guid>? Ids, DateOnly? PagoEm);

public record RecusaNaBaixaEmLote(Guid Id, string Motivo);

public record ResultadoDaBaixaEmLote(int Baixados, List<RecusaNaBaixaEmLote> Recusados);
