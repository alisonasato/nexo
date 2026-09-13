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
/// As operações avançadas sobre o que o escritório tem a receber: lançar em
/// parcelas, renegociar e baixar em lote.
///
/// <para>
/// Segundo PR da fase 1 do módulo financeiro, descrito no FINANCEIRO.md. Tudo
/// aqui se apoia no primeiro: a situação do recebível como trava de
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
        var grupo = rotas.MapGroup("/recebiveis").WithTags("Recebíveis");

        grupo.MapPost("/parcelamentos", Parcelar)
            .WithName("ParcelarRecebivel")
            .WithSummary("Lança um valor dividido em parcelas mensais")
            .WithDescription("Os centavos que sobram da divisão vão para a primeira parcela, e cada vencimento parte da primeira data. Com uma parcela só, é um lançamento avulso comum.")
            .Produces<ParcelamentoCriado>(StatusCodes.Status201Created)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPost("/{id:guid}/renegociar", Renegociar)
            .WithName("RenegociarRecebivel")
            .WithSummary("Troca um título em aberto por parcelas novas")
            .WithDescription("O título original vira renegociado e continua ocupando a competência; as parcelas novas apontam para ele. Juros e multa acrescentam, desconto abate.")
            .Produces<RenegociacaoCriada>(StatusCodes.Status201Created)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapPost("/baixar-em-lote", BaixarEmLote)
            .WithName("BaixarRecebiveisEmLote")
            .WithSummary("Registra o recebimento de vários recebíveis de uma vez")
            .WithDescription("Cada recebível é baixado ou recusado por conta própria: um já pago não impede os outros. O valor recebido é o valor cobrado.")
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

        /* A mesma conferência do lançamento avulso: cliente com papel de
           cliente, descrição, valor e competência. Parcelar não afrouxa nada. */
        var problemas = await Recebiveis.Conferir(
            new DadosDoAvulso(dados.PessoaId, dados.Descricao, total, dados.PrimeiroVencimento,
                dados.CompetenciaAno, dados.CompetenciaMes),
            banco, cancelamento);

        problemas.AddRange(ConferirParcelas(total, dados.Parcelas));

        if (problemas.Count > 0)
            return Results.Json(new RespostaComProblemas(problemas), statusCode: 422);

        var pessoa = await banco.Pessoas.FirstAsync(p => p.Id == dados.PessoaId, cancelamento);
        var agora = DateTimeOffset.UtcNow;

        /* Com uma parcela só não há grupo: "1 de 1" é ruído na tela. */
        var emGrupo = dados.Parcelas > 1;
        var grupo = Guid.NewGuid();

        var novos = Parcelas.Dividir(total, dados.Parcelas, dados.PrimeiroVencimento)
            .Select(parcela => new Recebivel
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                PessoaId = pessoa.Id,
                Pessoa = pessoa,
                ContratoId = null,
                CompetenciaAno = dados.CompetenciaAno,
                CompetenciaMes = dados.CompetenciaMes,
                Descricao = dados.Descricao.Trim(),
                Valor = parcela.Valor,
                Vencimento = parcela.Vencimento,
                Situacao = SituacaoRecebivel.Aberto,
                ParcelamentoId = emGrupo ? grupo : null,
                ParcelaNumero = emGrupo ? parcela.Numero : null,
                ParcelasTotal = emGrupo ? dados.Parcelas : null,
                CriadoEm = agora,
                AtualizadoEm = agora,
            })
            .ToList();

        banco.Recebiveis.AddRange(novos);
        await banco.SaveChangesAsync(cancelamento);

        return Results.Json(
            new ParcelamentoCriado(emGrupo ? grupo : null, novos.Select(Recebiveis.Detalhar).ToList()),
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
        var original = await banco.Recebiveis
            .Include(r => r.Pessoa)
            .FirstOrDefaultAsync(r => r.Id == id, cancelamento);

        if (original is null) return Results.NotFound();

        switch (original.Situacao)
        {
            case SituacaoRecebivel.Pago:
                return Problema("id", "Recebível já baixado",
                    $"Este recebível foi baixado em {original.PagoEm:dd/MM/yyyy}.",
                    "Título pago não se renegocia. Se a baixa estava errada, estorne antes.");

            case SituacaoRecebivel.Cancelado:
                return Problema("id", "Recebível cancelado",
                    "Um recebível cancelado não pode ser renegociado.",
                    "Se o valor voltou a ser devido, lance uma cobrança nova.");

            case SituacaoRecebivel.Renegociado:
                return Problema("id", "Recebível já renegociado",
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

        original.Situacao = SituacaoRecebivel.Renegociado;
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
            .Select(parcela => new Recebivel
            {
                Id = Guid.NewGuid(),
                TenantId = original.TenantId,
                PessoaId = original.PessoaId,
                Pessoa = original.Pessoa,
                ContratoId = null,
                CompetenciaAno = original.CompetenciaAno,
                CompetenciaMes = original.CompetenciaMes,
                Descricao = original.Descricao,
                Valor = parcela.Valor,
                Vencimento = parcela.Vencimento,
                Situacao = SituacaoRecebivel.Aberto,
                ParcelamentoId = emGrupo ? grupo : null,
                ParcelaNumero = emGrupo ? parcela.Numero : null,
                ParcelasTotal = emGrupo ? dados.Parcelas : null,
                RenegociadoDeId = original.Id,
                CriadoEm = agora,
                AtualizadoEm = agora,
            })
            .ToList();

        banco.Renegociacoes.Add(renegociacao);
        banco.Recebiveis.AddRange(parcelas);

        try
        {
            await banco.SaveChangesAsync(cancelamento);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problema("situacao", "O recebível mudou enquanto isto era feito",
                "Outra operação alterou este recebível no mesmo instante, e nada foi renegociado.",
                "Recarregue a lista e confira a situação antes de tentar de novo.");
        }
        catch (DbUpdateException erro) when (erro.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        })
        {
            return Problema("id", "Recebível já renegociado",
                "Este título foi renegociado por outra operação no mesmo instante.",
                "Recarregue a lista: as parcelas novas já estão lá.");
        }

        return Results.Json(
            new RenegociacaoCriada(renegociacao.Id, original.Id, parcelas.Select(Recebiveis.Detalhar).ToList()),
            statusCode: StatusCodes.Status201Created);
    }

    /* -------------------------------------------------- baixar em lote */

    /// <summary>
    /// Baixa vários recebíveis de uma vez, pelo valor cobrado.
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
                "Nenhum recebível foi informado.",
                "Marque na lista os recebíveis a baixar.");
        }

        if (ids.Count > MaximoPorLote)
        {
            return Problema("ids", "Seleção grande demais",
                $"Foram informados {ids.Count} recebíveis.",
                $"Baixe no máximo {MaximoPorLote} de cada vez.");
        }

        var pagoEm = dados.PagoEm ?? DateOnly.FromDateTime(DateTime.Today);
        var registro = registros.CreateLogger("Cobranca");
        var recusados = new List<RecusaNaBaixaEmLote>();
        var baixados = 0;

        foreach (var id in ids)
        {
            /* Um item recusado não pode deixar alterações pendentes para o
               próximo gravar junto. */
            banco.ChangeTracker.Clear();

            var recebivel = await banco.Recebiveis.FirstOrDefaultAsync(r => r.Id == id, cancelamento);

            if (recebivel is null)
            {
                recusados.Add(new RecusaNaBaixaEmLote(id, "Não encontrado."));
                continue;
            }

            if (recebivel.Situacao != SituacaoRecebivel.Aberto)
            {
                recusados.Add(new RecusaNaBaixaEmLote(id, recebivel.Situacao switch
                {
                    SituacaoRecebivel.Pago => $"Já baixado em {recebivel.PagoEm:dd/MM/yyyy}.",
                    SituacaoRecebivel.Cancelado => "Cancelado.",
                    SituacaoRecebivel.Renegociado => "Renegociado: baixe as parcelas novas.",
                    _ => "Não está em aberto.",
                }));
                continue;
            }

            if (await Cobrancas.TentarRetirarDoPsp(recebivel, asaas, configuracao.Value, registro, cancelamento)
                is { } falha)
            {
                recusados.Add(new RecusaNaBaixaEmLote(id, $"{falha.Titulo}: {falha.Descricao}"));
                continue;
            }

            recebivel.Situacao = SituacaoRecebivel.Pago;
            recebivel.ValorPago = recebivel.Valor;
            recebivel.PagoEm = pagoEm;
            recebivel.OrigemDaBaixa = OrigensDeBaixa.Lote;
            recebivel.AtualizadoEm = DateTimeOffset.UtcNow;

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

/// <param name="ValorTotal">O valor inteiro, antes de dividir.</param>
/// <param name="CompetenciaAno">O mês do serviço, igual para todas as parcelas.</param>
public record DadosDoParcelamento(
    Guid PessoaId,
    string Descricao,
    decimal ValorTotal,
    int Parcelas,
    DateOnly PrimeiroVencimento,
    int CompetenciaAno,
    int CompetenciaMes);

/// <param name="ParcelamentoId">Nulo quando foi lançada uma parcela só.</param>
public record ParcelamentoCriado(Guid? ParcelamentoId, List<RecebivelNaLista> Parcelas);

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

public record RenegociacaoCriada(Guid RenegociacaoId, Guid OrigemId, List<RecebivelNaLista> Parcelas);

/// <param name="PagoEm">Quando o dinheiro entrou. Vazio é hoje.</param>
public record DadosDaBaixaEmLote(List<Guid>? Ids, DateOnly? PagoEm);

public record RecusaNaBaixaEmLote(Guid Id, string Motivo);

public record ResultadoDaBaixaEmLote(int Baixados, List<RecusaNaBaixaEmLote> Recusados);
