using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// O código da pessoa: número puro, sem prefixo e sem zeros à esquerda.
///
/// <para>
/// É escolha de quem usa, não detalhe técnico — o código do cliente é o que se
/// fala ao telefone, e número curto é mais fácil de dizer. O do contrato segue
/// <c>C0001</c>, com prefixo e preenchimento, porque aparece em documento.
/// </para>
/// <para>
/// <b>Tirar os zeros custa a ordenação, e é isso que estes testes guardam.</b>
/// Ordenado como texto, "10" vem antes de "2" — a lista embaralha a partir do
/// décimo cliente, que chega cedo, e ninguém liga o defeito à mudança de
/// formato meses depois.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class CodigoDaPessoa(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public void O_primeiro_codigo_e_apenas_um()
    {
        Assert.Equal("1", Codigos.Proximo(string.Empty, [], digitos: 0));
    }

    [Fact]
    public void A_sequencia_segue_o_maior_e_nao_ganha_zeros()
    {
        Assert.Equal("10", Codigos.Proximo(string.Empty, ["1", "2", "9"], digitos: 0));
        Assert.Equal("41", Codigos.Proximo(string.Empty, ["7", "40", "12"], digitos: 0));
    }

    [Fact]
    public void O_contrato_continua_com_prefixo_e_preenchimento()
    {
        /* O formato dos dois é diferente de propósito, e isso precisa ficar
           preso: unificar por descuido mudaria código que já foi impresso. */
        Assert.Equal("C0001", Codigos.Proximo("C", [], digitos: 4));
        Assert.Equal("C0042", Codigos.Proximo("C", ["C0041"], digitos: 4));
    }

    [Fact]
    public async Task A_listagem_sai_em_ordem_numerica_e_nao_alfabetica()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        /* Doze clientes: o bastante para o texto e o número discordarem. */
        for (var i = 0; i < 12; i++) await CriarCliente(http);

        var pagina = await http.GetFromJsonAsync<PaginaDePessoas>("/pessoas?papel=Cliente&tamanho=50", Json);
        var codigos = pagina!.Itens.Select(pessoa => pessoa.Codigo).OrderBy(c => c.Length).ThenBy(c => c).ToList();

        Assert.Equal(
            ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12"],
            codigos);

        /*
         * A asserção acima falha sem a ordenação por tamanho: viria 1, 10, 11,
         * 12, 2, 3… que é a ordem de dicionário, correta para texto e errada
         * para quem procura o cliente 2.
         */
        Assert.Equal(codigos.OrderBy(c => int.Parse(c)), codigos);
    }

    private static async Task CriarCliente(HttpClient http)
    {
        var resposta = await http.PostAsJsonAsync("/pessoas", new DadosDePessoa(
            TipoPessoa.Juridica, RegimeTributario.SimplesNacional, string.Empty, [Papel.Cliente],
            "Cliente " + Guid.NewGuid().ToString("N")[..8], string.Empty,
            Documentos.CnpjValido(), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            null, string.Empty, true), Json);

        resposta.EnsureSuccessStatusCode();
    }
}
