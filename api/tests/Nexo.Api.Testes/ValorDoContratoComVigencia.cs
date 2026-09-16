using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// O valor do contrato com vigência, e o reajuste que abre a próxima.
///
/// <para>
/// O que se confere é o que faria o escritório cobrar o valor errado sem
/// perceber: a mensalidade de um mês antigo saindo com o preço de hoje, o
/// reajuste rodado duas vezes compondo o percentual, o contrato suspenso
/// reajustado junto, e a correção de um valor digitado errado virando histórico
/// de reajuste que nunca houve.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class ValorDoContratoComVigencia(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task O_contrato_nasce_com_uma_vigencia_e_corrigir_nao_cria_outra()
    {
        var http = await Entrar();
        var contrato = await CriarContrato(http, 1_000m);

        var inicial = Assert.Single(await Valores(http, contrato));
        Assert.Equal(1_000m, inicial.Valor);
        Assert.Equal((2026, 1), (inicial.VigenteDeAno, inicial.VigenteDeMes));
        Assert.Null(inicial.Percentual);

        /* Corrigir um valor digitado errado não é reajuste: não abre vigência nova. */
        (await Alterar(http, contrato, 1_200m)).EnsureSuccessStatusCode();

        var depois = Assert.Single(await Valores(http, contrato));
        Assert.Equal(1_200m, depois.Valor);
        Assert.Equal(1_200m, (await Obter(http, contrato)).Valor);
    }

    [Fact]
    public async Task O_reajuste_abre_vigencia_e_a_mensalidade_sai_com_o_valor_da_competencia()
    {
        var http = await Entrar();
        var contrato = await CriarContrato(http, 1_000m);

        var reajuste = await Reajustar(http, 10m, 2026, 4, "IPCA de 2026", [contrato]);
        Assert.Equal(1, reajuste.Reajustados);

        var vigencias = await Valores(http, contrato);
        Assert.Equal(2, vigencias.Count);
        Assert.Equal(1_100m, vigencias[0].Valor);
        Assert.Equal((2026, 4), (vigencias[0].VigenteDeAno, vigencias[0].VigenteDeMes));
        Assert.Equal(10m, vigencias[0].Percentual);
        Assert.Equal("IPCA de 2026", vigencias[0].Motivo);

        /* Março é anterior ao reajuste, e sai pelo valor de antes; abril já sai pelo novo. */
        await Gerar(http, 2026, 3);
        await Gerar(http, 2026, 4);

        Assert.Equal(1_000m, (await Mensalidade(http, 2026, 3)).Valor);
        Assert.Equal(1_100m, (await Mensalidade(http, 2026, 4)).Valor);
    }

    [Fact]
    public async Task Reajustar_duas_vezes_a_mesma_competencia_nao_compoe_o_percentual()
    {
        var http = await Entrar();
        var contrato = await CriarContrato(http, 1_000m);

        Assert.Equal(1, (await Reajustar(http, 10m, 2026, 4, "IPCA de 2026", [contrato])).Reajustados);

        var segunda = await Reajustar(http, 10m, 2026, 4, "IPCA de 2026", [contrato]);
        Assert.Equal((0, 1), (segunda.Reajustados, segunda.Ignorados));

        var vigencias = await Valores(http, contrato);
        Assert.Equal(2, vigencias.Count);
        Assert.Equal(1_100m, vigencias[0].Valor);
    }

    [Fact]
    public async Task Suspenso_fica_de_fora_e_o_reajuste_sem_percentual_ou_sem_motivo_e_recusado()
    {
        var http = await Entrar();
        var contrato = await CriarContrato(http, 1_000m);

        (await http.PutAsJsonAsync($"/contratos/{contrato}", new DadosDeContrato(
            await ClienteDoContrato(http, contrato), "Honorários contábeis", 1_000m, 10,
            new DateOnly(2026, 1, 1), null, SituacaoContrato.Suspenso, string.Empty), Json))
            .EnsureSuccessStatusCode();

        var resultado = await Reajustar(http, 10m, 2026, 4, "IPCA de 2026", [contrato]);
        Assert.Equal((0, 1), (resultado.Reajustados, resultado.ForaDeVigencia));
        Assert.Single(await Valores(http, contrato));

        Assert.Equal("percentual", await CampoRecusado(
            await http.PostAsJsonAsync("/contratos/reajustar",
                new PedidoDeReajuste(0m, 2026, 4, "IPCA de 2026", null), Json)));

        Assert.Equal("motivo", await CampoRecusado(
            await http.PostAsJsonAsync("/contratos/reajustar",
                new PedidoDeReajuste(10m, 2026, 4, " ", null), Json)));
    }

    /* --------------------------------------------------------- apoio */

    private async Task<HttpClient> Entrar() =>
        await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

    /// <summary>Um contrato de honorários, vigente desde janeiro de 2026, com vencimento no dia 10.</summary>
    private static async Task<Guid> CriarContrato(HttpClient http, decimal valor)
    {
        var pessoa = await http.PostAsJsonAsync("/pessoas", new DadosDePessoa(
            TipoPessoa.Juridica, RegimeTributario.SimplesNacional, string.Empty, [Papel.Cliente],
            "Cliente do teste", string.Empty, Documentos.CnpjValido(), string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, null, string.Empty, true), Json);
        pessoa.EnsureSuccessStatusCode();
        var pessoaId = (await pessoa.Content.ReadFromJsonAsync<PessoaDetalhada>(Json))!.Id;

        var contrato = await http.PostAsJsonAsync("/contratos", new DadosDeContrato(
            pessoaId, "Honorários contábeis", valor, 10,
            new DateOnly(2026, 1, 1), null, SituacaoContrato.Ativo, string.Empty), Json);
        contrato.EnsureSuccessStatusCode();

        return (await contrato.Content.ReadFromJsonAsync<ContratoDetalhado>(Json))!.Id;
    }

    private static async Task<Guid> ClienteDoContrato(HttpClient http, Guid contrato) =>
        (await Obter(http, contrato)).PessoaId;

    private static async Task<ContratoDetalhado> Obter(HttpClient http, Guid contrato) =>
        (await http.GetFromJsonAsync<ContratoDetalhado>($"/contratos/{contrato}", Json))!;

    private static async Task<HttpResponseMessage> Alterar(HttpClient http, Guid contrato, decimal valor)
    {
        var atual = await Obter(http, contrato);

        return await http.PutAsJsonAsync($"/contratos/{contrato}", new DadosDeContrato(
            atual.PessoaId, atual.Descricao, valor, atual.DiaDeVencimento,
            atual.InicioDaVigencia, atual.FimDaVigencia, atual.Situacao, atual.Observacoes), Json);
    }

    private static async Task<List<ValorNaLista>> Valores(HttpClient http, Guid contrato) =>
        (await http.GetFromJsonAsync<List<ValorNaLista>>($"/contratos/{contrato}/valores", Json))!;

    private static async Task<ResultadoDoReajuste> Reajustar(
        HttpClient http, decimal percentual, int ano, int mes, string motivo, List<Guid>? contratos)
    {
        var resposta = await http.PostAsJsonAsync("/contratos/reajustar",
            new PedidoDeReajuste(percentual, ano, mes, motivo, contratos), Json);

        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<ResultadoDoReajuste>(Json))!;
    }

    private static async Task Gerar(HttpClient http, int ano, int mes) =>
        (await http.PostAsJsonAsync("/contratos/gerar-mensalidades",
            new PedidoDeGeracao(ano, mes, null), Json)).EnsureSuccessStatusCode();

    private static async Task<LancamentoNaLista> Mensalidade(HttpClient http, int ano, int mes)
    {
        var pagina = (await http.GetFromJsonAsync<PaginaDeLancamentos>(
            $"/lancamentos?natureza=Receber&ano={ano}&mes={mes}", Json))!;

        return Assert.Single(pagina.Itens);
    }

    private static async Task<string> CampoRecusado(HttpResponseMessage resposta)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>(Json);
        return Assert.Single(corpo!.Problemas).Campo;
    }
}
