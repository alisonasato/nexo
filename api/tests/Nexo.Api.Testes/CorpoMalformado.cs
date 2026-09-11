using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// Corpo que a API não consegue ler.
///
/// <para>
/// Só acontece com cliente defeituoso — o front é gerado do contrato e não erra
/// o formato. É <b>por isso</b> que a mensagem importa: quem topa com ela está
/// depurando um cliente novo, e um 400 sem estrutura nesse momento troca um
/// problema por dois.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class CorpoMalformado(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    public void Dispose() => _aplicacao.Dispose();

    private static StringContent Json(string bruto) =>
        new(bruto, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Json_quebrado_devolve_400_no_formato_de_problemas()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        var resposta = await cliente.PostAsync("/pessoas", Json("{ isto nao e json"));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);

        var corpo = await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>();
        var problema = Assert.Single(corpo!.Problemas);

        Assert.Equal("corpo", problema.Campo);
        Assert.Equal("Requisição malformada", problema.Titulo);

        /* A sugestão precisa dizer para onde olhar, não só que deu errado. */
        Assert.Contains("/swagger", problema.Sugestao);
    }

    [Fact]
    public async Task Tipo_errado_num_campo_tambem_cai_no_mesmo_formato()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        /* `valor` é decimal no contrato, e aqui vai um texto. */
        var resposta = await cliente.PostAsync("/contratos",
            Json("""{"clienteId":"00000000-0000-0000-0000-000000000000","valor":"muito caro"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);

        var corpo = await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>();
        Assert.Equal("corpo", Assert.Single(corpo!.Problemas).Campo);
    }

    [Fact]
    public async Task Corpo_bem_formado_mas_invalido_segue_devolvendo_422()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        /*
         * A distinção que importa. 400 é "não consegui ler"; 422 é "li e não
         * serve". Achatar os dois faria o cliente tratar erro de digitação do
         * usuário como defeito de programação.
         */
        var resposta = await cliente.PostAsync("/contratos",
            Json("""
                 {"clienteId":"00000000-0000-0000-0000-000000000000","descricao":"",
                  "valor":0,"diaDeVencimento":99,"inicioDaVigencia":"2026-01-01",
                  "fimDaVigencia":null,"situacao":"Ativo","observacoes":""}
                 """));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);

        var corpo = await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>();
        Assert.NotEmpty(corpo!.Problemas);
        Assert.DoesNotContain(corpo.Problemas, problema => problema.Campo == "corpo");
    }
}
