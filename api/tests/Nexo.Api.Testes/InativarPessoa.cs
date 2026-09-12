using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// Inativar é sair do cadastro, e sair com conta em aberto é o que estes
/// testes impedem.
///
/// <para>
/// O defeito que motivou: inativar não conferia nada. Um cliente com contrato
/// ativo sumia da listagem e o contrato continuava gerando mensalidade todo
/// mês — cobrança recorrente para alguém que ninguém mais via. E quem ficou
/// devendo sumia levando a dívida da tela junto.
/// </para>
/// <para>
/// O que estes testes fixam não é "tem papel, não pode": é o vínculo vivo que
/// impede, e o papel sozinho não impede nada. A diferença importa, porque
/// quase toda pessoa carrega papel, e recusar por papel tornaria inativar
/// impossível na prática.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class InativarPessoa(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Papel_sozinho_nao_impede_de_inativar()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var pessoa = await CriarPessoa(http, "Cliente que nunca contratou nada");

        var resposta = await http.DeleteAsync($"/pessoas/{pessoa}");

        /*
         * É o caso que separa "vínculo vivo" de "tem rótulo". A pessoa está
         * marcada como cliente e não deve nada; recusar aqui transformaria o
         * papel numa prisão.
         */
        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.False((await Obter(http, pessoa)).Ativo);
    }

    [Fact]
    public async Task Contrato_nao_encerrado_impede_e_a_mensagem_diz_qual()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var pessoa = await CriarPessoa(http, "Padaria com contrato vivo");
        var contrato = await CriarContrato(http, pessoa, 500m);

        var resposta = await http.DeleteAsync($"/pessoas/{pessoa}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);

        var problema = Assert.Single(await Problemas(resposta));
        Assert.Equal("contratos", problema.Campo);
        Assert.Contains("Padaria com contrato vivo", problema.Descricao);
        Assert.Contains(contrato.Codigo, problema.Descricao);
        Assert.Contains("Encerre o contrato", problema.Sugestao);

        /* Recusar e gravar assim mesmo seria o pior dos dois mundos. */
        Assert.True((await Obter(http, pessoa)).Ativo);
    }

    [Fact]
    public async Task Contrato_suspenso_tambem_impede()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var pessoa = await CriarPessoa(http, "Cliente em pausa");
        await CriarContrato(http, pessoa, 300m, SituacaoContrato.Suspenso);

        var resposta = await http.DeleteAsync($"/pessoas/{pessoa}");

        /*
         * Suspenso é pausa, não fim: o acordo ainda existe e a expectativa é
         * voltar. Deixar inativar aqui deixaria o contrato num limbo, preso a
         * alguém que saiu do cadastro.
         */
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
    }

    [Fact]
    public async Task Encerrar_o_contrato_libera_a_inativacao()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var pessoa = await CriarPessoa(http, "Cliente que foi embora");
        var contrato = await CriarContrato(http, pessoa, 800m);

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await http.DeleteAsync($"/pessoas/{pessoa}")).StatusCode);

        await Encerrar(http, contrato);

        /*
         * A saída existe, e é a escrituração certa. Sem ela a recusa seria
         * armadilha: bloqueio permanente sem nada que o destrave.
         */
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await http.DeleteAsync($"/pessoas/{pessoa}")).StatusCode);
    }

    [Fact]
    public async Task Cobranca_em_aberto_impede_e_a_mensagem_diz_quanto()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var pessoa = await CriarPessoa(http, "Quem ficou devendo");
        await CriarAvulso(http, pessoa, 1_500m);

        var resposta = await http.DeleteAsync($"/pessoas/{pessoa}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);

        var problema = Assert.Single(await Problemas(resposta));
        Assert.Equal("recebiveis", problema.Campo);

        /*
         * O valor em pt-BR é assertivo de propósito: se o contêiner subisse sem
         * a cultura, a dívida sairia como "$1,500.00", e este teste cai antes de
         * alguém ver isso em produção.
         */
        Assert.Contains("R$ 1.500,00", problema.Descricao);
    }

    [Fact]
    public async Task Duas_cobrancas_viram_uma_soma_e_nao_duas_mensagens()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var pessoa = await CriarPessoa(http, "Quem deve duas vezes");
        await CriarAvulso(http, pessoa, 1_000m);
        await CriarAvulso(http, pessoa, 250m);

        var resposta = await http.DeleteAsync($"/pessoas/{pessoa}");
        var problema = Assert.Single(await Problemas(resposta));

        Assert.Contains("2 cobranças em aberto", problema.Descricao);
        Assert.Contains("R$ 1.250,00", problema.Descricao);
    }

    [Fact]
    public async Task Cobranca_baixada_deixa_de_impedir()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var pessoa = await CriarPessoa(http, "Quem acertou a conta");
        var cobranca = await CriarAvulso(http, pessoa, 400m);

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await http.DeleteAsync($"/pessoas/{pessoa}")).StatusCode);

        var baixa = await http.PostAsJsonAsync($"/recebiveis/{cobranca}/baixar",
            new DadosDaBaixa(400m, new DateOnly(2026, 3, 5)), Json);
        baixa.EnsureSuccessStatusCode();

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await http.DeleteAsync($"/pessoas/{pessoa}")).StatusCode);
    }

    [Fact]
    public async Task Contrato_e_cobranca_juntos_dao_dois_problemas()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var pessoa = await CriarPessoa(http, "Quem tem os dois");
        await CriarContrato(http, pessoa, 600m);
        await CriarAvulso(http, pessoa, 600m);

        var resposta = await http.DeleteAsync($"/pessoas/{pessoa}");

        /* Uma recusa só, dizendo tudo o que falta, e não uma por vez. */
        Assert.Equal(2, (await Problemas(resposta)).Count);
    }

    /* --------------------------------------------------------- apoio */

    private static async Task<List<Problema>> Problemas(HttpResponseMessage resposta) =>
        (await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>(Json))!.Problemas;

    private static async Task<PessoaDetalhada> Obter(HttpClient http, Guid id) =>
        (await http.GetFromJsonAsync<PessoaDetalhada>($"/pessoas/{id}", Json))!;

    private static async Task<Guid> CriarPessoa(HttpClient http, string nome)
    {
        var resposta = await http.PostAsJsonAsync("/pessoas", new DadosDePessoa(
            TipoPessoa.Juridica, RegimeTributario.SimplesNacional, string.Empty, [Papel.Cliente],
            nome, string.Empty, Documentos.CnpjValido(), string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, null, string.Empty, true), Json);

        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<PessoaDetalhada>(Json))!.Id;
    }

    private static async Task<ContratoDetalhado> CriarContrato(
        HttpClient http,
        Guid pessoa,
        decimal valor,
        SituacaoContrato situacao = SituacaoContrato.Ativo)
    {
        var resposta = await http.PostAsJsonAsync("/contratos", new DadosDeContrato(
            pessoa, "Honorários contábeis", valor, 10,
            new DateOnly(2026, 1, 1), null, situacao, string.Empty), Json);

        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<ContratoDetalhado>(Json))!;
    }

    private static async Task Encerrar(HttpClient http, ContratoDetalhado contrato)
    {
        var resposta = await http.PutAsJsonAsync($"/contratos/{contrato.Id}", new DadosDeContrato(
            contrato.PessoaId, contrato.Descricao, contrato.Valor, contrato.DiaDeVencimento,
            contrato.InicioDaVigencia, contrato.FimDaVigencia,
            SituacaoContrato.Encerrado, contrato.Observacoes), Json);

        resposta.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CriarAvulso(HttpClient http, Guid pessoa, decimal valor)
    {
        var resposta = await http.PostAsJsonAsync("/recebiveis", new DadosDoAvulso(
            pessoa, "Serviço avulso", valor, new DateOnly(2026, 3, 10), 2026, 2), Json);

        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<RecebivelNaLista>(Json))!.Id;
    }
}
