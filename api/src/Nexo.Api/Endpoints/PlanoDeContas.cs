using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dados;
using Nexo.Api.Dominio;
using Npgsql;

namespace Nexo.Api.Endpoints;

/// <summary>
/// O plano de contas: as categorias em árvore e os centros de custo.
/// </summary>
public static class PlanoDeContas
{
    private const string FalhaNaCategoria = "Falha na validação da categoria";
    private const string FalhaNoCentro = "Falha na validação do centro de custo";

    private static readonly StringComparer Alfabetica = StringComparer.Create(CultureInfo.GetCultureInfo("pt-BR"), ignoreCase: true);

    /// <summary>
    /// O plano que um escritório de contabilidade pequeno costuma usar, para não
    /// começar do zero. É ponto de partida: tudo nele se renomeia, se inativa e
    /// se completa depois.
    /// </summary>
    private static readonly (NaturezaLancamento Natureza, (string Grupo, string[] Categorias)[] Grupos)[] PlanoSugerido =
    [
        (NaturezaLancamento.Receber,
        [
            ("Honorários", ["Mensalidades contábeis", "Serviços avulsos"]),
            ("Outras receitas", ["Rendimentos financeiros", "Reembolsos de clientes"]),
        ]),
        (NaturezaLancamento.Pagar,
        [
            ("Pessoal", ["Salários", "Encargos e benefícios", "Pró-labore"]),
            ("Ocupação", ["Aluguel e condomínio", "Energia, água e internet"]),
            ("Operação", ["Sistemas e licenças", "Serviços de terceiros", "Material de escritório"]),
            ("Impostos e taxas", ["Tributos sobre o faturamento", "Taxas e anuidades"]),
            ("Financeiro", ["Tarifas bancárias", "Juros e multas pagos"]),
        ]),
    ];

    public static IEndpointRouteBuilder MapPlanoDeContas(this IEndpointRouteBuilder rotas)
    {
        var categorias = rotas.MapGroup("/categorias").WithTags("Plano de contas");

        categorias.MapGet("/", ListarCategorias)
            .WithName("ListarCategorias")
            .WithSummary("Lista as categorias, em ordem de árvore")
            .WithDescription("Cada categoria vem logo depois da de cima, com o nível e o caminho inteiro. A receber antes de a pagar.")
            .Produces<List<CategoriaNaLista>>();

        categorias.MapPost("/", CriarCategoria)
            .WithName("CriarCategoria")
            .WithSummary("Cria uma categoria")
            .WithDescription("Na raiz, a natureza é obrigatória. Debaixo de outra, a natureza é a dela.")
            .Produces<CategoriaNaLista>(StatusCodes.Status201Created)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity);

        categorias.MapPut("/{id:guid}", AlterarCategoria)
            .WithName("AlterarCategoria")
            .WithSummary("Renomeia, move ou inativa uma categoria")
            .WithDescription("A natureza não muda, e a categoria não vai para dentro de si mesma nem passa de três níveis com o que está abaixo dela.")
            .Produces<CategoriaNaLista>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        categorias.MapPost("/plano-sugerido", CriarPlanoSugerido)
            .WithName("CriarPlanoSugerido")
            .WithSummary("Cria o plano de contas sugerido para escritório de contabilidade")
            .WithDescription("Só num plano vazio: completar um plano que já existe misturaria duas formas de classificar.")
            .Produces<List<CategoriaNaLista>>(StatusCodes.Status201Created)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity);

        var centros = rotas.MapGroup("/centros-de-custo").WithTags("Plano de contas");

        centros.MapGet("/", ListarCentros)
            .WithName("ListarCentrosDeCusto")
            .WithSummary("Lista os centros de custo")
            .WithDescription("Os ativos primeiro, e dentro de cada grupo pelo nome.")
            .Produces<List<CentroNaLista>>();

        centros.MapPost("/", CriarCentro)
            .WithName("CriarCentroDeCusto")
            .WithSummary("Cria um centro de custo")
            .Produces<CentroNaLista>(StatusCodes.Status201Created)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity);

        centros.MapPut("/{id:guid}", AlterarCentro)
            .WithName("AlterarCentroDeCusto")
            .WithSummary("Renomeia, inativa ou reativa um centro de custo")
            .Produces<CentroNaLista>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        return rotas;
    }

    /* -------------------------------------------------------- categorias */

    private static async Task<IResult> ListarCategorias(NexoDbContext banco, CancellationToken cancelamento)
    {
        var todas = await banco.Categorias.AsNoTracking().ToListAsync(cancelamento);
        return Results.Ok(EmArvore(todas));
    }

    /// <summary>
    /// A lista na ordem de árvore, com o nível e o caminho de cada uma.
    ///
    /// <para>
    /// Montada aqui, e não numa consulta recursiva: um plano de contas tem
    /// dezenas de linhas, e a ordem de árvore com o caminho por extenso é mais
    /// clara em dez linhas de C# do que numa CTE que ninguém vai querer mexer.
    /// </para>
    /// </summary>
    internal static List<CategoriaNaLista> EmArvore(IReadOnlyCollection<Categoria> todas)
    {
        var filhas = todas.ToLookup(categoria => categoria.PaiId);
        var resultado = new List<CategoriaNaLista>(todas.Count);

        void Descer(Guid? pai, int nivel, string caminho)
        {
            /* O limite protege de um ciclo que só existiria se alguém mexesse no banco à mão. */
            if (nivel > Categoria.NiveisMaximos + 1) return;

            foreach (var categoria in filhas[pai]
                         .OrderBy(categoria => categoria.Natureza)
                         .ThenBy(categoria => categoria.Nome, Alfabetica))
            {
                var proprio = caminho.Length == 0 ? categoria.Nome : $"{caminho} › {categoria.Nome}";

                resultado.Add(new CategoriaNaLista(
                    categoria.Id,
                    categoria.Nome,
                    categoria.Natureza,
                    categoria.PaiId,
                    nivel,
                    proprio,
                    categoria.Ativa,
                    filhas[categoria.Id].Any()));

                Descer(categoria.Id, nivel + 1, proprio);
            }
        }

        Descer(null, 1, string.Empty);
        return resultado;
    }

    private static async Task<IResult> CriarCategoria(
        [FromBody] DadosDaCategoria dados,
        NexoDbContext banco,
        IContextoDeTenant contexto,
        CancellationToken cancelamento)
    {
        if (contexto.TenantAtual is not { } tenant) return Results.Unauthorized();

        var todas = await banco.Categorias.AsNoTracking().ToListAsync(cancelamento);

        var problemas = ConferirCategoria(dados, atual: null, todas);
        if (problemas.Count > 0) return Results.UnprocessableEntity(new RespostaComProblemas(problemas));

        var pai = dados.PaiId is { } paiId ? todas.Single(categoria => categoria.Id == paiId) : null;
        var agora = DateTimeOffset.UtcNow;

        var nova = new Categoria
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            Nome = dados.Nome!.Trim(),
            Natureza = pai?.Natureza ?? dados.Natureza,
            PaiId = pai?.Id,
            Ativa = true,
            CriadoEm = agora,
            AtualizadoEm = agora,
        };

        banco.Categorias.Add(nova);
        await banco.SaveChangesAsync(cancelamento);

        todas.Add(nova);
        return Results.Created($"/categorias/{nova.Id}", EmArvore(todas).Single(categoria => categoria.Id == nova.Id));
    }

    private static async Task<IResult> AlterarCategoria(
        Guid id,
        [FromBody] DadosDaCategoria dados,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var categoria = await banco.Categorias.FirstOrDefaultAsync(categoria => categoria.Id == id, cancelamento);
        if (categoria is null) return Results.NotFound();

        var todas = await banco.Categorias.AsNoTracking().ToListAsync(cancelamento);

        var problemas = ConferirCategoria(dados, categoria, todas);
        if (problemas.Count > 0) return Results.UnprocessableEntity(new RespostaComProblemas(problemas));

        categoria.Nome = dados.Nome!.Trim();
        categoria.PaiId = dados.PaiId;
        categoria.Ativa = dados.Ativa;
        categoria.AtualizadoEm = DateTimeOffset.UtcNow;

        await banco.SaveChangesAsync(cancelamento);

        var atualizadas = await banco.Categorias.AsNoTracking().ToListAsync(cancelamento);
        return Results.Ok(EmArvore(atualizadas).Single(outra => outra.Id == id));
    }

    /// <param name="atual">A categoria sendo alterada; nula ao criar.</param>
    /// <param name="todas">O plano inteiro, como está no banco antes desta gravação.</param>
    private static List<Problema> ConferirCategoria(DadosDaCategoria dados, Categoria? atual, List<Categoria> todas)
    {
        var problemas = new List<Problema>();
        var nome = dados.Nome?.Trim() ?? string.Empty;

        if (nome.Length == 0)
        {
            problemas.Add(new Problema("nome", FalhaNaCategoria,
                "O nome da categoria não foi informado.",
                "Use o nome com que o escritório fala dela: “Aluguel e condomínio”, “Honorários”."));
        }
        else if (nome.Length > 60)
        {
            problemas.Add(new Problema("nome", FalhaNaCategoria,
                $"O nome tem {nome.Length} caracteres.",
                "Use até 60."));
        }

        Categoria? pai = null;

        if (dados.PaiId is { } paiId)
        {
            pai = todas.FirstOrDefault(categoria => categoria.Id == paiId);

            if (pai is null)
            {
                problemas.Add(new Problema("paiId", "Categoria de cima não encontrada",
                    "A categoria escolhida para ficar acima não existe neste escritório.",
                    "Escolha uma categoria da lista."));
                return problemas;
            }
        }

        /*
         * A natureza de quem está sendo gravada: a de cima, quando há; a própria,
         * quando é alteração; a pedida, quando é raiz nova. Zero no corpo quer
         * dizer "herde", e só é problema na raiz nova.
         */
        var pedida = Enum.IsDefined(dados.Natureza) ? dados.Natureza : (NaturezaLancamento?)null;
        var natureza = atual?.Natureza ?? pai?.Natureza ?? pedida;

        if (atual is not null && pedida is { } outra && outra != atual.Natureza)
        {
            problemas.Add(new Problema("natureza", "A natureza não muda",
                "Uma categoria não troca de natureza depois de criada: o que já foi classificado nela mudaria de lado.",
                "Crie outra categoria com a natureza certa e inative esta."));
        }

        if (natureza is null)
        {
            problemas.Add(new Problema("natureza", FalhaNaCategoria,
                "Não foi dito se a categoria é a receber ou a pagar.",
                "Informe a natureza: Receber ou Pagar."));
            return problemas;
        }

        /*
         * Compara com a natureza de quem está sendo gravada, e não com a
         * variável acima: ela já herdou a da categoria de cima, e compará-la com
         * a própria de cima nunca acusaria diferença. Ao criar, vale a pedida;
         * ao mover, a que a categoria já tem.
         */
        if (pai is not null && (atual?.Natureza ?? pedida) is { } propria && propria != pai.Natureza)
        {
            problemas.Add(new Problema(atual is null ? "natureza" : "paiId",
                "Natureza diferente da categoria de cima",
                $"“{pai.Nome}” é {Rotulo(pai.Natureza)}, e tudo abaixo dela também é.",
                "Escolha uma categoria de cima com a mesma natureza, ou deixe esta na raiz."));
            return problemas;
        }

        if (atual is not null && pai is not null && (pai.Id == atual.Id || EstaDentro(pai, atual.Id, todas)))
        {
            problemas.Add(new Problema("paiId", "Categoria dentro de si mesma",
                "A categoria escolhida para ficar acima está dentro desta.",
                "Escolha uma categoria de outro ramo do plano."));
            return problemas;
        }

        /* Quem se move leva junto o que está abaixo: é a altura do galho inteiro que precisa caber. */
        var nivel = pai is null ? 1 : Nivel(pai, todas) + 1;
        var altura = atual is null ? 1 : Altura(atual.Id, todas, 1);

        if (nivel + altura - 1 > Categoria.NiveisMaximos)
        {
            problemas.Add(new Problema("paiId", "Fundo demais",
                $"O plano vai até {Categoria.NiveisMaximos} níveis, e ali a categoria passaria disso.",
                "Escolha uma categoria mais acima, ou deixe esta na raiz."));
        }

        var repetida = todas.Any(outra =>
            outra.Id != atual?.Id
            && outra.PaiId == pai?.Id
            && outra.Natureza == natureza
            && string.Equals(outra.Nome, nome, StringComparison.OrdinalIgnoreCase));

        if (nome.Length > 0 && repetida)
        {
            problemas.Add(new Problema("nome", "Categoria já existe",
                $"Já existe “{nome}” neste mesmo lugar do plano.",
                "Dê outro nome, ou use a que já existe."));
        }

        return problemas;
    }

    private static int Nivel(Categoria categoria, List<Categoria> todas)
    {
        var nivel = 1;
        var acima = categoria.PaiId;

        while (acima is { } id && nivel <= Categoria.NiveisMaximos + 1
               && todas.FirstOrDefault(outra => outra.Id == id) is { } pai)
        {
            nivel++;
            acima = pai.PaiId;
        }

        return nivel;
    }

    private static int Altura(Guid id, List<Categoria> todas, int profundidade)
    {
        if (profundidade > Categoria.NiveisMaximos + 1) return profundidade;

        var filhas = todas.Where(categoria => categoria.PaiId == id).ToList();
        return filhas.Count == 0 ? 1 : 1 + filhas.Max(filha => Altura(filha.Id, todas, profundidade + 1));
    }

    /// <summary>Se a categoria está em algum ponto abaixo de <paramref name="ancestral"/>.</summary>
    private static bool EstaDentro(Categoria categoria, Guid ancestral, List<Categoria> todas)
    {
        var acima = categoria.PaiId;

        for (var passo = 0; acima is { } id && passo <= Categoria.NiveisMaximos + 1; passo++)
        {
            if (id == ancestral) return true;
            acima = todas.FirstOrDefault(outra => outra.Id == id)?.PaiId;
        }

        return false;
    }

    private static string Rotulo(NaturezaLancamento natureza) =>
        natureza == NaturezaLancamento.Pagar ? "a pagar" : "a receber";

    private static async Task<IResult> CriarPlanoSugerido(
        NexoDbContext banco,
        IContextoDeTenant contexto,
        CancellationToken cancelamento)
    {
        if (contexto.TenantAtual is not { } tenant) return Results.Unauthorized();

        if (await banco.Categorias.AnyAsync(cancelamento))
        {
            return Results.UnprocessableEntity(new RespostaComProblemas([
                new Problema("categorias", "O plano já existe",
                    "Este escritório já tem categorias, e o plano sugerido só entra num plano vazio.",
                    "Complete o plano atual criando as categorias que faltam."),
            ]));
        }

        var agora = DateTimeOffset.UtcNow;
        var novas = new List<Categoria>();

        Categoria Nova(string nome, NaturezaLancamento natureza, Guid? paiId) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            Nome = nome,
            Natureza = natureza,
            PaiId = paiId,
            Ativa = true,
            CriadoEm = agora,
            AtualizadoEm = agora,
        };

        foreach (var (natureza, grupos) in PlanoSugerido)
        {
            foreach (var (grupo, filhas) in grupos)
            {
                var raiz = Nova(grupo, natureza, null);
                novas.Add(raiz);
                novas.AddRange(filhas.Select(filha => Nova(filha, natureza, raiz.Id)));
            }
        }

        banco.Categorias.AddRange(novas);
        await banco.SaveChangesAsync(cancelamento);

        return Results.Json(EmArvore(novas), statusCode: StatusCodes.Status201Created);
    }

    /* ---------------------------------------------------- centros de custo */

    private static async Task<IResult> ListarCentros(NexoDbContext banco, CancellationToken cancelamento)
    {
        var centros = await banco.CentrosDeCusto.AsNoTracking()
            .OrderByDescending(centro => centro.Ativo)
            .ThenBy(centro => centro.Nome)
            .ThenBy(centro => centro.Id)
            .Select(centro => new CentroNaLista(centro.Id, centro.Nome, centro.Ativo))
            .ToListAsync(cancelamento);

        return Results.Ok(centros);
    }

    private static async Task<IResult> CriarCentro(
        [FromBody] DadosDoCentro dados,
        NexoDbContext banco,
        IContextoDeTenant contexto,
        CancellationToken cancelamento)
    {
        if (contexto.TenantAtual is not { } tenant) return Results.Unauthorized();

        var problemas = await ConferirCentro(dados, Guid.Empty, banco, cancelamento);
        if (problemas.Count > 0) return Results.UnprocessableEntity(new RespostaComProblemas(problemas));

        var agora = DateTimeOffset.UtcNow;
        var centro = new CentroDeCusto
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            Nome = dados.Nome!.Trim(),
            Ativo = true,
            CriadoEm = agora,
            AtualizadoEm = agora,
        };

        banco.CentrosDeCusto.Add(centro);
        if (await GravarCentro(banco, cancelamento) is { } recusa) return recusa;

        return Results.Created($"/centros-de-custo/{centro.Id}", new CentroNaLista(centro.Id, centro.Nome, centro.Ativo));
    }

    private static async Task<IResult> AlterarCentro(
        Guid id,
        [FromBody] DadosDoCentro dados,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var centro = await banco.CentrosDeCusto.FirstOrDefaultAsync(centro => centro.Id == id, cancelamento);
        if (centro is null) return Results.NotFound();

        var problemas = await ConferirCentro(dados, id, banco, cancelamento);
        if (problemas.Count > 0) return Results.UnprocessableEntity(new RespostaComProblemas(problemas));

        centro.Nome = dados.Nome!.Trim();
        centro.Ativo = dados.Ativo;
        centro.AtualizadoEm = DateTimeOffset.UtcNow;

        if (await GravarCentro(banco, cancelamento) is { } recusa) return recusa;

        return Results.Ok(new CentroNaLista(centro.Id, centro.Nome, centro.Ativo));
    }

    private static async Task<List<Problema>> ConferirCentro(
        DadosDoCentro dados,
        Guid proprio,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var problemas = new List<Problema>();
        var nome = dados.Nome?.Trim() ?? string.Empty;

        if (nome.Length == 0)
        {
            problemas.Add(new Problema("nome", FalhaNoCentro,
                "O nome do centro de custo não foi informado.",
                "Use o nome da unidade, da área ou do projeto: “Matriz”, “Filial Centro”, “Departamento pessoal”."));
        }
        else if (nome.Length > 60)
        {
            problemas.Add(new Problema("nome", FalhaNoCentro, $"O nome tem {nome.Length} caracteres.", "Use até 60."));
        }
        else
        {
            var minusculo = nome.ToLower();
            var repetido = await banco.CentrosDeCusto.AnyAsync(
                centro => centro.Id != proprio && centro.Nome.ToLower() == minusculo, cancelamento);

            if (repetido)
            {
                problemas.Add(new Problema("nome", "Centro de custo já cadastrado",
                    $"Já existe um centro de custo chamado “{nome}”.",
                    "Dê um nome que diferencie os dois."));
            }
        }

        return problemas;
    }

    /// <summary>Grava, e transforma o nome repetido que escapou da conferência, por gravação simultânea, em recusa legível.</summary>
    private static async Task<IResult?> GravarCentro(NexoDbContext banco, CancellationToken cancelamento)
    {
        try
        {
            await banco.SaveChangesAsync(cancelamento);
            return null;
        }
        catch (DbUpdateException erro) when (erro.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        })
        {
            return Results.UnprocessableEntity(new RespostaComProblemas([
                new Problema("nome", "Centro de custo já cadastrado",
                    "Outro centro de custo com este nome foi gravado no mesmo instante.",
                    "Recarregue a lista e escolha outro nome."),
            ]));
        }
    }
}

/// <param name="Natureza">Obrigatória na raiz. Debaixo de outra categoria, pode vir vazia: a natureza é a de cima.</param>
/// <param name="PaiId">A categoria de cima. Vazia, a categoria fica na raiz.</param>
/// <param name="Ativa">Ignorado ao criar: categoria nasce ativa.</param>
public record DadosDaCategoria(string? Nome, NaturezaLancamento Natureza, Guid? PaiId, bool Ativa = true);

/// <param name="Nivel">1 na raiz, até 3.</param>
/// <param name="Caminho">O nome com todos os de cima: “Pessoal › Encargos e benefícios”.</param>
/// <param name="TemFilhas">Com filhas, a categoria não desce mais do que os níveis delas deixam.</param>
public record CategoriaNaLista(
    Guid Id,
    string Nome,
    NaturezaLancamento Natureza,
    Guid? PaiId,
    int Nivel,
    string Caminho,
    bool Ativa,
    bool TemFilhas);

/// <param name="Ativo">Ignorado ao criar: centro de custo nasce ativo.</param>
public record DadosDoCentro(string? Nome, bool Ativo = true);

public record CentroNaLista(Guid Id, string Nome, bool Ativo);
