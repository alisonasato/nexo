using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dados;
using Nexo.Api.Dominio;

namespace Nexo.Api.Endpoints;

/// <summary>
/// O cadastro de pessoas: a primeira tela de verdade.
///
/// Nenhuma consulta aqui filtra por tenant. Não é esquecimento — é o desenho
/// da decisão Q22: quem filtra é a política de RLS, alimentada pela claim da
/// sessão. Escrever o filtro à mão daria a impressão de que ele é o que
/// protege, e o dia em que alguém esquecesse seria o dia do vazamento.
/// </summary>
public static class Pessoas
{
    public static IEndpointRouteBuilder MapPessoas(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/pessoas").WithTags("Pessoas");

        grupo.MapGet("/", Listar)
            .WithName("ListarPessoas")
            .WithSummary("Lista as pessoas do tenant")
            .Produces<PaginaDePessoas>();

        grupo.MapGet("/{id:guid}", Obter)
            .WithName("ObterPessoa")
            .WithSummary("Abre um cadastro")
            .Produces<PessoaDetalhada>()
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapPost("/", Criar)
            .WithName("CriarPessoa")
            .WithSummary("Cria um cadastro")
            .Produces<PessoaDetalhada>(StatusCodes.Status201Created)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPut("/{id:guid}", Alterar)
            .WithName("AlterarPessoa")
            .WithSummary("Altera um cadastro")
            .Produces<PessoaDetalhada>()
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapDelete("/{id:guid}", Inativar)
            .WithName("InativarPessoa")
            .WithSummary("Inativa um cadastro")
            .WithDescription("Não apaga: marca como inativo. Quem já apareceu em contrato ou cobrança precisa continuar existindo no histórico. Recusa enquanto houver contrato não encerrado ou cobrança em aberto.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<RespostaComProblemas>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        grupo.MapPost("/{id:guid}/reativar", Reativar)
            .WithName("ReativarPessoa")
            .WithSummary("Desfaz a inativação")
            .WithDescription("Existe como endereço próprio, e não como um PUT com ativo=true, porque o PUT reescreve o cadastro inteiro: a tela precisaria mandar de volta campos que ela não tem em mãos, e apagaria o que não conhece.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return rotas;
    }

    private static async Task<IResult> Listar(
        NexoDbContext banco,
        CancellationToken cancelamento,
        [FromQuery] string? busca = null,
        [FromQuery] Papel? papel = null,
        [FromQuery] bool incluirInativos = false,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 25,
        [FromQuery] OrdemDaListagem ordenarPor = OrdemDaListagem.Nome,
        [FromQuery] Direcao direcao = Direcao.Crescente)
    {
        pagina = Math.Max(1, pagina);
        tamanho = Math.Clamp(tamanho, 1, 200);

        IQueryable<Pessoa> consulta = banco.Pessoas.AsNoTracking().Include(pessoa => pessoa.Papeis);

        if (!incluirInativos)
            consulta = consulta.Where(pessoa => pessoa.Ativo);

        /* Filtrar por papel é o que substitui a antiga listagem de clientes. */
        if (papel is { } procurado)
            consulta = consulta.Where(pessoa => pessoa.Papeis.Any(p => p.Papel == procurado));

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim();

            /*
             * O termo vira documento do mesmo jeito que o documento é guardado:
             * maiúsculas, sem pontuação, letras preservadas. Limpar para
             * dígitos deixaria de achar o CNPJ alfanumérico — "12ABC34501DE35"
             * viraria "123450135", que não é o documento de ninguém.
             */
            var documento = Documento.NormalizarCnpj(termo);

            /*
             * A busca cobre os três jeitos de alguém procurar um cadastro: pelo
             * nome, pelo nome fantasia ou pelo documento. O documento é
             * guardado sem pontuação, então digitar o CNPJ formatado também
             * encontra.
             */
            consulta = consulta.Where(pessoa =>
                EF.Functions.ILike(pessoa.Nome, $"%{termo}%")
                || EF.Functions.ILike(pessoa.NomeFantasia, $"%{termo}%")
                || (documento.Length > 0 && EF.Functions.Like(pessoa.Documento, $"%{documento}%")));
        }

        var total = await consulta.CountAsync(cancelamento);

        var itens = await Ordenar(consulta, ordenarPor, direcao)
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .Select(pessoa => new PessoaNaLista(
                pessoa.Id,
                pessoa.Codigo,
                pessoa.Papeis.Select(p => p.Papel).ToList(),
                pessoa.Tipo,
                pessoa.Nome,
                pessoa.NomeFantasia,
                pessoa.Documento,
                pessoa.Email,
                pessoa.Telefone,
                pessoa.Celular,
                new DadosDeEndereco(
                    pessoa.Endereco.Cep,
                    pessoa.Endereco.Logradouro,
                    pessoa.Endereco.Numero,
                    pessoa.Endereco.Complemento,
                    pessoa.Endereco.Bairro,
                    pessoa.Endereco.Cidade,
                    pessoa.Endereco.Uf),
                pessoa.Ativo))
            .ToListAsync(cancelamento);

        return Results.Ok(new PaginaDePessoas(itens, total, pagina, tamanho));
    }

    /// <summary>
    /// A ordem da listagem, decidida no banco.
    ///
    /// <para>
    /// <b>Ordenar no navegador ordenaria a página, não a lista.</b> Com 25 de
    /// 300, classificar por nome no cliente produziria uma ordem perfeita
    /// dentro de um recorte arbitrário — e a página 2 traria nomes que deviam
    /// vir antes dos da página 1. O resultado é plausível e errado, que é o
    /// jeito mais caro de errar. É a mesma razão pela qual nenhum total desta
    /// aplicação é somado na tela.
    /// </para>
    /// <para>
    /// <b>Código ordena pelo comprimento antes do texto.</b> Ele é número
    /// guardado como texto, e em texto "10" vem antes de "2". Comparar o
    /// tamanho primeiro devolve a ordem numérica sem converter nada — e
    /// converter seria pior: um código vazio derrubaria a consulta inteira.
    /// </para>
    /// </summary>
    private static IOrderedQueryable<Pessoa> Ordenar(
        IQueryable<Pessoa> consulta,
        OrdemDaListagem por,
        Direcao direcao)
    {
        var decrescente = direcao == Direcao.Decrescente;

        if (por == OrdemDaListagem.Codigo)
        {
            return decrescente
                ? consulta.OrderByDescending(pessoa => pessoa.Codigo.Length)
                    .ThenByDescending(pessoa => pessoa.Codigo)
                : consulta.OrderBy(pessoa => pessoa.Codigo.Length)
                    .ThenBy(pessoa => pessoa.Codigo);
        }

        return decrescente
            ? consulta.OrderByDescending(pessoa => pessoa.Nome)
            : consulta.OrderBy(pessoa => pessoa.Nome);
    }

    private static async Task<IResult> Obter(Guid id, NexoDbContext banco, CancellationToken cancelamento)
    {
        var pessoa = await banco.Pessoas.AsNoTracking()
            .Include(pessoa => pessoa.Papeis)
            .FirstOrDefaultAsync(pessoa => pessoa.Id == id, cancelamento);

        return pessoa is null ? Results.NotFound() : Results.Ok(Detalhar(pessoa));
    }

    private static async Task<IResult> Criar(
        [FromBody] DadosDePessoa dados,
        NexoDbContext banco,
        IContextoDeTenant contexto,
        CancellationToken cancelamento)
    {
        if (contexto.TenantAtual is not { } tenant)
            return Results.Unauthorized();

        var pessoa = new Pessoa { Id = Guid.NewGuid(), TenantId = tenant };
        Aplicar(dados, pessoa);

        var problemas = await Conferir(pessoa, banco, cancelamento);
        if (problemas.Count > 0)
            return Results.Json(new RespostaComProblemas(problemas), statusCode: 422);

        /*
         * Os códigos já usados são lidos sob a política de RLS: a sequência é
         * por escritório, e um tenant nunca enxerga o número do outro.
         */
        var codigos = await banco.Pessoas.AsNoTracking()
            .Select(outra => outra.Codigo)
            .ToListAsync(cancelamento);

        pessoa.Codigo = Codigos.Proximo(string.Empty, codigos, digitos: 0);
        pessoa.CriadoEm = DateTimeOffset.UtcNow;
        pessoa.AtualizadoEm = pessoa.CriadoEm;

        AjustarPapeis(pessoa, dados.Papeis, tenant);
        banco.Pessoas.Add(pessoa);
        await banco.SaveChangesAsync(cancelamento);

        return Results.Created($"/pessoas/{pessoa.Id}", Detalhar(pessoa));
    }

    private static async Task<IResult> Alterar(
        Guid id,
        [FromBody] DadosDePessoa dados,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var pessoa = await banco.Pessoas
            .Include(pessoa => pessoa.Papeis)
            .FirstOrDefaultAsync(pessoa => pessoa.Id == id, cancelamento);

        if (pessoa is null) return Results.NotFound();

        Aplicar(dados, pessoa);
        AjustarPapeis(pessoa, dados.Papeis, pessoa.TenantId);

        var problemas = await Conferir(pessoa, banco, cancelamento);
        if (problemas.Count > 0)
            return Results.Json(new RespostaComProblemas(problemas), statusCode: 422);

        pessoa.AtualizadoEm = DateTimeOffset.UtcNow;
        await banco.SaveChangesAsync(cancelamento);

        return Results.Ok(Detalhar(pessoa));
    }

    private static async Task<IResult> Inativar(Guid id, NexoDbContext banco, CancellationToken cancelamento)
    {
        var pessoa = await banco.Pessoas.FirstOrDefaultAsync(pessoa => pessoa.Id == id, cancelamento);
        if (pessoa is null) return Results.NotFound();

        var impedimentos = await Impedimentos(pessoa, banco, cancelamento);
        if (impedimentos.Count > 0)
        {
            return Results.UnprocessableEntity(new RespostaComProblemas(impedimentos));
        }

        pessoa.Ativo = false;
        pessoa.AtualizadoEm = DateTimeOffset.UtcNow;
        await banco.SaveChangesAsync(cancelamento);

        return Results.NoContent();
    }

    private static async Task<IResult> Reativar(Guid id, NexoDbContext banco, CancellationToken cancelamento)
    {
        var pessoa = await banco.Pessoas.FirstOrDefaultAsync(pessoa => pessoa.Id == id, cancelamento);
        if (pessoa is null) return Results.NotFound();

        /*
         * Reativar quem já está ativa não é erro: é o segundo clique de quem
         * não viu o primeiro chegar. Devolver 422 aqui faria a tela mostrar
         * problema onde o estado desejado já é o estado atual.
         */
        pessoa.Ativo = true;
        pessoa.AtualizadoEm = DateTimeOffset.UtcNow;
        await banco.SaveChangesAsync(cancelamento);

        return Results.NoContent();
    }

    /// <summary>
    /// O que impede este cadastro de sair de circulação.
    ///
    /// <para>
    /// <b>Não é o papel que impede, é o vínculo vivo.</b> Quase toda pessoa
    /// carrega algum papel — é para isso que eles existem —, então recusar por
    /// papel tornaria inativar quase impossível, que é pior do que não conferir
    /// nada. O que dói são outras duas coisas: contrato não encerrado continua
    /// gerando mensalidade todo mês para alguém que sumiu da lista, e cobrança
    /// em aberto é dinheiro a receber que some da tela sem ninguém ter decidido
    /// isso.
    /// </para>
    /// <para>
    /// <b>Recusa, e não avisa.</b> Aviso vira segundo clique, e segundo clique
    /// vira hábito — e o custo do hábito aqui é cobrança recorrente invisível.
    /// Não é armadilha como um índice cego seria, porque a saída existe e é a
    /// escrituração certa: encerrar o contrato e dar baixa ou cancelar a
    /// cobrança é o que de fato aconteceu quando um cliente foi embora.
    /// </para>
    /// </summary>
    private static async Task<List<Problema>> Impedimentos(
        Pessoa pessoa,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        var problemas = new List<Problema>();

        var contratos = await banco.Contratos
            .Where(contrato => contrato.PessoaId == pessoa.Id
                && contrato.Situacao != SituacaoContrato.Encerrado)
            .OrderBy(contrato => contrato.Codigo)
            .Select(contrato => contrato.Codigo)
            .ToListAsync(cancelamento);

        if (contratos.Count == 1)
        {
            problemas.Add(new Problema("contratos", TitulosDeProblema.Vinculo,
                $"“{pessoa.Nome}” tem o contrato {contratos[0]} ainda não encerrado.",
                "Encerre o contrato antes de inativar o cadastro."));
        }
        else if (contratos.Count > 1)
        {
            problemas.Add(new Problema("contratos", TitulosDeProblema.Vinculo,
                $"“{pessoa.Nome}” tem {contratos.Count} contratos ainda não encerrados ({Citar(contratos)}).",
                "Encerre os contratos antes de inativar o cadastro."));
        }

        /*
         * Quantidade e soma saem do banco numa consulta só. Trazer as linhas
         * para somar aqui daria o mesmo número hoje e o número errado no dia em
         * que alguém paginasse — é a mesma regra que vale para os totais da tela.
         */
        var abertas = await banco.Recebiveis
            .Where(recebivel => recebivel.PessoaId == pessoa.Id
                && recebivel.Situacao == SituacaoRecebivel.Aberto)
            .GroupBy(recebivel => 1)
            .Select(grupo => new { Quantidade = grupo.Count(), Total = grupo.Sum(r => r.Valor) })
            .FirstOrDefaultAsync(cancelamento);

        if (abertas is { Quantidade: 1 })
        {
            problemas.Add(new Problema("recebiveis", TitulosDeProblema.Vinculo,
                $"“{pessoa.Nome}” tem uma cobrança em aberto, de {EmReais(abertas.Total)}.",
                "Dê baixa ou cancele a cobrança antes de inativar o cadastro."));
        }
        else if (abertas is { Quantidade: > 1 })
        {
            problemas.Add(new Problema("recebiveis", TitulosDeProblema.Vinculo,
                $"“{pessoa.Nome}” tem {abertas.Quantidade} cobranças em aberto, somando {EmReais(abertas.Total)}.",
                "Dê baixa ou cancele as cobranças antes de inativar o cadastro."));
        }

        return problemas;
    }

    /// <summary>
    /// Os códigos, com um teto. Um cadastro antigo pode ter dezenas de
    /// contratos, e uma mensagem com dezenas de códigos não é mais informativa
    /// que uma com cinco — é só mais difícil de ler.
    /// </summary>
    private static string Citar(IReadOnlyList<string> codigos)
    {
        const int teto = 5;
        if (codigos.Count <= teto) return string.Join(", ", codigos);

        return string.Join(", ", codigos.Take(teto)) + $" e mais {codigos.Count - teto}";
    }

    /// <summary>
    /// Reais por extenso na mensagem. A cultura vai explícita porque a do
    /// servidor não é a do escritório: o contêiner sobe em inglês, e sem isto a
    /// dívida de mil e quinhentos apareceria como "$1,500.00".
    /// </summary>
    private static string EmReais(decimal valor) =>
        valor.ToString("C", CultureInfo.GetCultureInfo("pt-BR"));

    /* ------------------------------------------------------------- apoio */

    private static async Task<List<Problema>> Conferir(
        Pessoa pessoa,
        NexoDbContext banco,
        CancellationToken cancelamento)
    {
        Pessoa? outra = null;

        if (pessoa.Documento.Length > 0)
        {
            /*
             * Sem filtro de tenant: a política já limita a busca ao tenant da
             * sessão, então "documento já cadastrado" nunca vaza a existência
             * de um cadastro de outro escritório.
             */
            outra = await banco.Pessoas.AsNoTracking()
                .FirstOrDefaultAsync(
                    candidata => candidata.Documento == pessoa.Documento && candidata.Id != pessoa.Id,
                    cancelamento);
        }

        return ValidadorDePessoa.Validar(pessoa, outra);
    }

    /// <summary>
    /// Acerta os papéis para ficarem exatamente os informados.
    ///
    /// <para>
    /// Tira o que saiu e põe o que entrou, em vez de apagar tudo e recriar. A
    /// diferença aparece no <c>CriadoEm</c>: recriando, um papel que ninguém
    /// mexeu ganharia data de hoje, e a única pergunta que esse campo responde
    /// — desde quando esta pessoa é cliente — passaria a mentir.
    /// </para>
    /// <para>
    /// Lista nula quer dizer "não mexi nos papéis", e é diferente de lista
    /// vazia, que quer dizer "tire todos". Sem essa distinção, um cliente
    /// enviado por um cliente HTTP antigo perderia os rótulos em silêncio.
    /// </para>
    /// </summary>
    private static void AjustarPapeis(Pessoa pessoa, IReadOnlyList<Papel>? desejados, Guid tenant)
    {
        if (desejados is null) return;

        var alvo = desejados.Distinct().ToHashSet();

        foreach (var sobrando in pessoa.Papeis.Where(p => !alvo.Contains(p.Papel)).ToList())
            pessoa.Papeis.Remove(sobrando);

        foreach (var novo in alvo.Where(p => pessoa.Papeis.All(atual => atual.Papel != p)))
            pessoa.Papeis.Add(new PessoaPapel { TenantId = tenant, PessoaId = pessoa.Id, Papel = novo });
    }

    private static void Aplicar(DadosDePessoa dados, Pessoa pessoa)
    {
        pessoa.Tipo = dados.Tipo;
        pessoa.Nome = (dados.Nome ?? string.Empty).Trim();
        pessoa.NomeFantasia = (dados.NomeFantasia ?? string.Empty).Trim();

        /*
         * Guardado sem pontuação, para a busca achar com ou sem ela. CPF vira
         * só dígitos; CNPJ preserva as letras, que existem desde 31/07/2026.
         */
        pessoa.Documento = dados.Tipo == TipoPessoa.Fisica
            ? Documento.ApenasDigitos(dados.Documento)
            : Documento.NormalizarCnpj(dados.Documento);

        pessoa.InscricaoEstadual = (dados.InscricaoEstadual ?? string.Empty).Trim();
        pessoa.InscricaoMunicipal = (dados.InscricaoMunicipal ?? string.Empty).Trim();
        pessoa.Email = (dados.Email ?? string.Empty).Trim();
        /*
         * Telefone entra como dígito, igual ao documento e ao CEP. A regra já
         * valia para os outros dois, e deixar o telefone de fora abriria duas
         * formas do mesmo dado no banco — quem guarda `(11) 98765-4321` e quem
         * guarda `11987654321` — e a busca teria de conhecer as duas.
         */
        pessoa.Telefone = Documento.ApenasDigitos(dados.Telefone);
        pessoa.Celular = Documento.ApenasDigitos(dados.Celular);
        pessoa.Observacoes = (dados.Observacoes ?? string.Empty).Trim();
        pessoa.RegimeTributario = dados.RegimeTributario;
        pessoa.Responsavel = (dados.Responsavel ?? string.Empty).Trim();
        pessoa.Ativo = dados.Ativo;

        pessoa.Endereco = new Endereco
        {
            Cep = Documento.ApenasDigitos(dados.Endereco?.Cep),
            Logradouro = (dados.Endereco?.Logradouro ?? string.Empty).Trim(),
            Numero = (dados.Endereco?.Numero ?? string.Empty).Trim(),
            Complemento = (dados.Endereco?.Complemento ?? string.Empty).Trim(),
            Bairro = (dados.Endereco?.Bairro ?? string.Empty).Trim(),
            Cidade = (dados.Endereco?.Cidade ?? string.Empty).Trim(),
            Uf = (dados.Endereco?.Uf ?? string.Empty).Trim().ToUpperInvariant(),
        };
    }

    private static PessoaDetalhada Detalhar(Pessoa pessoa) => new(
        pessoa.Id,
        pessoa.Codigo,
        pessoa.Papeis.Select(p => p.Papel).OrderBy(p => p).ToList(),
        pessoa.RegimeTributario,
        pessoa.Responsavel,
        pessoa.Tipo,
        pessoa.Nome,
        pessoa.NomeFantasia,
        pessoa.Documento,
        pessoa.InscricaoEstadual,
        pessoa.InscricaoMunicipal,
        pessoa.Email,
        pessoa.Telefone,
        pessoa.Celular,
        new DadosDeEndereco(
            pessoa.Endereco.Cep,
            pessoa.Endereco.Logradouro,
            pessoa.Endereco.Numero,
            pessoa.Endereco.Complemento,
            pessoa.Endereco.Bairro,
            pessoa.Endereco.Cidade,
            pessoa.Endereco.Uf),
        pessoa.Observacoes,
        pessoa.Ativo,
        pessoa.CriadoEm);
}

public record DadosDeEndereco(
    string Cep,
    string Logradouro,
    string Numero,
    string Complemento,
    string Bairro,
    string Cidade,
    string Uf);

public record DadosDePessoa(
    TipoPessoa Tipo,
    RegimeTributario RegimeTributario,
    string? Responsavel,
    IReadOnlyList<Papel>? Papeis,
    string? Nome,
    string? NomeFantasia,
    string? Documento,
    string? InscricaoEstadual,
    string? InscricaoMunicipal,
    string? Email,
    string? Telefone,
    string? Celular,
    DadosDeEndereco? Endereco,
    string? Observacoes,
    bool Ativo);

/// <summary>
/// A pessoa como a listagem precisa dela.
///
/// <para>
/// O endereço vem inteiro, e não só cidade e UF: a coluna da tela mostra a
/// linha toda. Montar essa linha aqui seria mais barato e pior — a mesma
/// pessoa aparece em tela, em etiqueta e em nota, cada uma com uma forma, e
/// quem escolhe a forma é quem exibe.
/// </para>
/// </summary>
public record PessoaNaLista(
    Guid Id,
    string Codigo,
    IReadOnlyList<Papel> Papeis,
    TipoPessoa Tipo,
    string Nome,
    string NomeFantasia,
    string Documento,
    string Email,
    string Telefone,
    string Celular,
    DadosDeEndereco Endereco,
    bool Ativo);

public record PessoaDetalhada(
    Guid Id,
    string Codigo,
    IReadOnlyList<Papel> Papeis,
    RegimeTributario RegimeTributario,
    string Responsavel,
    TipoPessoa Tipo,
    string Nome,
    string NomeFantasia,
    string Documento,
    string InscricaoEstadual,
    string InscricaoMunicipal,
    string Email,
    string Telefone,
    string Celular,
    DadosDeEndereco Endereco,
    string Observacoes,
    bool Ativo,
    DateTimeOffset CriadoEm);

/// <summary>Por qual coluna a listagem é ordenada.</summary>
public enum OrdemDaListagem
{
    Nome = 1,
    Codigo = 2,
}

public enum Direcao
{
    Crescente = 1,
    Decrescente = 2,
}

public record PaginaDePessoas(List<PessoaNaLista> Itens, int Total, int Pagina, int Tamanho);

public record RespostaComProblemas(List<Problema> Problemas);
