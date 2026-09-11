using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Api.Servicos;

namespace Nexo.Api.Testes;

/// <summary>
/// A consulta de CEP pela borda HTTP, com o serviço de fora dublado.
///
/// <para>
/// O ViaCEP <b>não</b> é chamado de verdade aqui. Um teste que sai para a
/// internet falha quando o serviço de terceiro cai, e aí deixa de dizer o que
/// prometia: passaria a medir o ViaCEP em vez do Nexo, e a falha vermelha não
/// significaria nada sobre este código.
/// </para>
/// <para>
/// O que está em prova é a tradução dos três desfechos em três respostas
/// diferentes. Achatar "não existe" e "não deu para perguntar" no mesmo código
/// faria a tela dizer a frase errada num dos dois casos — e a frase errada aqui
/// manda a pessoa conferir um CEP que estava certo.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class ConsultaDeCepTeste(BancoDeTestes banco) : IDisposable
{
    private readonly List<AplicacaoDeTestes> _abertas = [];

    public void Dispose()
    {
        foreach (var aplicacao in _abertas) aplicacao.Dispose();
    }

    private AplicacaoDeTestes ComConsulta(RespostaDoCep resposta)
    {
        var aplicacao = new AplicacaoDeTestes(banco.Conexao, servicos =>
            servicos.AddSingleton<IConsultaDeCep>(new ConsultaDublada(resposta)));

        _abertas.Add(aplicacao);
        return aplicacao;
    }

    [Fact]
    public async Task Cep_encontrado_devolve_o_endereco()
    {
        var aplicacao = ComConsulta(new RespostaDoCep(
            ResultadoDaConsulta.Encontrado,
            new EnderecoDoCep("01310100", "Avenida Paulista", "Bela Vista", "São Paulo", "SP")));

        var http = await Contas.Entrar(aplicacao, await Contas.Criar(banco, aplicacao));

        var endereco = await http.GetFromJsonAsync<EnderecoDoCep>("/enderecos/01310-100");

        Assert.Equal("Avenida Paulista", endereco!.Logradouro);
        Assert.Equal("São Paulo", endereco.Cidade);
        Assert.Equal("SP", endereco.Uf);
    }

    [Fact]
    public async Task Cep_inexistente_devolve_404()
    {
        var aplicacao = ComConsulta(new RespostaDoCep(ResultadoDaConsulta.NaoEncontrado));
        var http = await Contas.Entrar(aplicacao, await Contas.Criar(banco, aplicacao));

        var resposta = await http.GetAsync("/enderecos/99999999");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Servico_fora_do_ar_devolve_503_e_nao_404()
    {
        var aplicacao = ComConsulta(new RespostaDoCep(ResultadoDaConsulta.Indisponivel));
        var http = await Contas.Entrar(aplicacao, await Contas.Criar(banco, aplicacao));

        var resposta = await http.GetAsync("/enderecos/01310100");

        /*
         * A diferença entre estes dois códigos é a diferença entre "confira o
         * número que você digitou" e "preencha à mão desta vez". Trocar um pelo
         * outro manda a pessoa procurar erro onde não há.
         */
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
    }

    [Fact]
    public async Task Cep_com_menos_de_oito_digitos_nem_chega_a_consultar()
    {
        var consulta = new ConsultaDublada(new RespostaDoCep(ResultadoDaConsulta.Indisponivel));

        var aplicacao = new AplicacaoDeTestes(banco.Conexao, servicos =>
            servicos.AddSingleton<IConsultaDeCep>(consulta));
        _abertas.Add(aplicacao);

        var http = await Contas.Entrar(aplicacao, await Contas.Criar(banco, aplicacao));

        var resposta = await http.GetAsync("/enderecos/1234");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);

        /* Bater no serviço de fora para descobrir o que já dava para ver aqui. */
        Assert.Equal(0, consulta.Chamadas);
    }

    [Fact]
    public async Task Sem_sessao_a_consulta_e_recusada()
    {
        var aplicacao = ComConsulta(new RespostaDoCep(ResultadoDaConsulta.NaoEncontrado));

        var resposta = await aplicacao.CreateClient().GetAsync("/enderecos/01310100");

        /* Vale a política padrão fechada: rota nova nasce exigindo sessão. */
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    private sealed class ConsultaDublada(RespostaDoCep resposta) : IConsultaDeCep
    {
        public int Chamadas { get; private set; }

        public Task<RespostaDoCep> BuscarAsync(string cep, CancellationToken cancelamento)
        {
            Chamadas++;
            return Task.FromResult(resposta);
        }
    }
}
