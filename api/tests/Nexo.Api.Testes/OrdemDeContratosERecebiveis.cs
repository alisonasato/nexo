using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// A ordem das listagens de contratos e de recebíveis.
///
/// <para>
/// <b>Ordenar é do banco, não da tela</b> — o mesmo motivo da listagem de
/// pessoas: com 25 linhas de 300, classificar no navegador daria uma ordem
/// perfeita dentro de um recorte arbitrário.
/// </para>
/// <para>
/// Aqui há um caso que pessoas não tem: <b>valor empata o tempo todo</b>.
/// Mensalidade gerada de contrato sai idêntica para a carteira inteira, e
/// ordenar por uma coluna que empata sem critério de desempate faz o banco
/// devolver as linhas em ordem livre a cada consulta. Paginando, isso não
/// embaralha: some com uma cobrança e mostra outra duas vezes.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class OrdemDeContratosERecebiveis(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Dispose() => _aplicacao.Dispose();

    /* ----------------------------------------------------- contratos */

    [Fact]
    public async Task Contratos_vem_por_codigo_e_o_decrescente_inverte()
    {
        var http = await Entrar();

        for (var i = 1; i <= 3; i++) await CriarContrato(http, "Cliente " + i, 100m * i, 10);

        /* O código de contrato vem preenchido com zeros, ao contrário do de
           pessoa: a ordem de texto já é a numérica enquanto a largura não
           mudar. Quem guarda o caso do C10000 é o comprimento, comparado antes
           do texto — e ele só aparece com dez mil contratos, que este teste não
           vai criar. */
        Assert.Equal(["C0001", "C0002", "C0003"], await Codigos(http, "?tamanho=50"));

        Assert.Equal(
            ["C0003", "C0002", "C0001"],
            await Codigos(http, "?tamanho=50&ordenarPor=Codigo&direcao=Decrescente"));
    }

    [Fact]
    public async Task Contratos_ordenam_por_cliente_valor_e_vencimento()
    {
        var http = await Entrar();

        await CriarContrato(http, "Zulu", valor: 300m, dia: 5);
        await CriarContrato(http, "Alfa", valor: 900m, dia: 25);
        await CriarContrato(http, "Mike", valor: 600m, dia: 15);

        Assert.Equal(["Alfa", "Mike", "Zulu"], await Clientes(http, "?ordenarPor=Cliente"));

        Assert.Equal(
            [900m, 600m, 300m],
            await Valores(http, "?ordenarPor=Valor&direcao=Decrescente"));

        Assert.Equal([5, 15, 25], await Vencimentos(http, "?ordenarPor=Vencimento"));
    }

    [Fact]
    public async Task Contratos_de_mesmo_valor_tem_ordem_definida_entre_si()
    {
        var http = await Entrar();

        /* Seis clientes na mesma faixa de honorário, que é o caso comum de um
           escritório — e o caso em que ordenar por valor não decide nada. */
        for (var i = 1; i <= 6; i++) await CriarContrato(http, "Cliente " + i, 500m, 10);

        var ids = (await Contratos(http, "?ordenarPor=Valor")).Itens.Select(c => c.Id).ToList();

        /*
         * O desempate é o identificador, e é ele que este teste confere — não o
         * sintoma. O sintoma é linha sumindo entre páginas, e ele não aparece
         * com seis registros: o Postgres devolve ordem estável enquanto o plano
         * não muda, então um teste de duas páginas passaria mesmo sem
         * desempate nenhum e não guardaria nada. Identificador é aleatório, e a
         * ordem de inserção não é a dele: exigir a ordem crescente aqui só
         * passa se o critério existir de fato.
         */
        Assert.Equal(ids.OrderBy(id => id).ToList(), ids);
    }

    /* ---------------------------------------------------- recebíveis */

    [Fact]
    public async Task Recebiveis_vem_por_vencimento()
    {
        var http = await Entrar();

        await CriarAvulso(http, "Zulu", 100m, new DateOnly(2026, 3, 20), 2026, 3);
        await CriarAvulso(http, "Alfa", 200m, new DateOnly(2026, 3, 5), 2026, 3);
        await CriarAvulso(http, "Mike", 300m, new DateOnly(2026, 3, 12), 2026, 3);

        Assert.Equal(
            [new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 12), new DateOnly(2026, 3, 20)],
            await Vencidos(http, ""));

        Assert.Equal(
            [new DateOnly(2026, 3, 20), new DateOnly(2026, 3, 12), new DateOnly(2026, 3, 5)],
            await Vencidos(http, "?ordenarPor=Vencimento&direcao=Decrescente"));
    }

    [Fact]
    public async Task Recebiveis_ordenam_por_cliente_e_por_valor()
    {
        var http = await Entrar();

        await CriarAvulso(http, "Zulu", 100m, new DateOnly(2026, 3, 5), 2026, 3);
        await CriarAvulso(http, "Alfa", 300m, new DateOnly(2026, 3, 20), 2026, 3);
        await CriarAvulso(http, "Mike", 200m, new DateOnly(2026, 3, 12), 2026, 3);

        Assert.Equal(["Alfa", "Mike", "Zulu"], await NomesDosRecebiveis(http, "?ordenarPor=Cliente"));
        Assert.Equal([300m, 200m, 100m], await ValoresDosRecebiveis(http, "?ordenarPor=Valor&direcao=Decrescente"));
    }

    [Fact]
    public async Task Competencia_ordena_por_ano_antes_do_mes()
    {
        var http = await Entrar();

        /*
         * Março de 2026 e dezembro de 2025. Ordenar pelo texto que a tela
         * mostra — "03/2026" e "12/2025" — poria março na frente, e o
         * fechamento do escritório sairia com o mês errado em cima.
         */
        await CriarAvulso(http, "Alfa", 100m, new DateOnly(2026, 4, 10), 2026, 3);
        await CriarAvulso(http, "Beta", 200m, new DateOnly(2026, 1, 10), 2025, 12);

        var pagina = await Recebiveis(http, "?ordenarPor=Competencia");
        var competencias = pagina.Itens
            .Select(r => (r.CompetenciaAno, r.CompetenciaMes))
            .ToList();

        Assert.Equal([(2025, 12), (2026, 3)], competencias);
    }

    [Fact]
    public async Task Recebiveis_de_mesmo_valor_tem_ordem_definida_entre_si()
    {
        var http = await Entrar();

        /* Seis mensalidades do mesmo plano, vencendo no mesmo dia: nem o valor
           nem o vencimento decidem. */
        for (var i = 1; i <= 6; i++)
            await CriarAvulso(http, "Cliente " + i, 500m, new DateOnly(2026, 3, 10), 2026, 3);

        var ids = await Ids(http, "?ordenarPor=Valor");

        Assert.Equal(ids.OrderBy(id => id).ToList(), ids);
    }

    /* --------------------------------------------------------- apoio */

    private async Task<HttpClient> Entrar() =>
        await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

    private static async Task<PaginaDeContratos> Contratos(HttpClient http, string consulta) =>
        (await http.GetFromJsonAsync<PaginaDeContratos>("/contratos" + consulta, Json))!;

    private static async Task<PaginaDeRecebiveis> Recebiveis(HttpClient http, string consulta) =>
        (await http.GetFromJsonAsync<PaginaDeRecebiveis>("/recebiveis" + consulta, Json))!;

    private static async Task<List<string>> Codigos(HttpClient http, string consulta) =>
        (await Contratos(http, consulta)).Itens.Select(c => c.Codigo).ToList();

    private static async Task<List<string>> Clientes(HttpClient http, string consulta) =>
        (await Contratos(http, consulta)).Itens.Select(c => c.NomeDaPessoa).ToList();

    private static async Task<List<decimal>> Valores(HttpClient http, string consulta) =>
        (await Contratos(http, consulta)).Itens.Select(c => c.Valor).ToList();

    private static async Task<List<int>> Vencimentos(HttpClient http, string consulta) =>
        (await Contratos(http, consulta)).Itens.Select(c => c.DiaDeVencimento).ToList();

    private static async Task<List<DateOnly>> Vencidos(HttpClient http, string consulta) =>
        (await Recebiveis(http, consulta)).Itens.Select(r => r.Vencimento).ToList();

    private static async Task<List<string>> NomesDosRecebiveis(HttpClient http, string consulta) =>
        (await Recebiveis(http, consulta)).Itens.Select(r => r.NomeDaPessoa).ToList();

    private static async Task<List<decimal>> ValoresDosRecebiveis(HttpClient http, string consulta) =>
        (await Recebiveis(http, consulta)).Itens.Select(r => r.Valor).ToList();

    private static async Task<List<Guid>> Ids(HttpClient http, string consulta) =>
        (await Recebiveis(http, consulta)).Itens.Select(r => r.Id).ToList();

    private static async Task<Guid> CriarPessoa(HttpClient http, string nome)
    {
        var resposta = await http.PostAsJsonAsync("/pessoas", new DadosDePessoa(
            TipoPessoa.Juridica, RegimeTributario.SimplesNacional, string.Empty, [Papel.Cliente],
            nome, string.Empty, Documentos.CnpjValido(), string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, null, string.Empty, true), Json);

        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<PessoaDetalhada>(Json))!.Id;
    }

    private static async Task CriarContrato(HttpClient http, string cliente, decimal valor, int dia)
    {
        var resposta = await http.PostAsJsonAsync("/contratos", new DadosDeContrato(
            await CriarPessoa(http, cliente), "Honorários contábeis", valor, dia,
            new DateOnly(2025, 1, 1), null, SituacaoContrato.Ativo, string.Empty), Json);

        resposta.EnsureSuccessStatusCode();
    }

    private static async Task CriarAvulso(
        HttpClient http, string cliente, decimal valor, DateOnly vencimento, int ano, int mes)
    {
        var resposta = await http.PostAsJsonAsync("/recebiveis", new DadosDoAvulso(
            await CriarPessoa(http, cliente), "Certidão", valor, vencimento, ano, mes), Json);

        resposta.EnsureSuccessStatusCode();
    }
}
