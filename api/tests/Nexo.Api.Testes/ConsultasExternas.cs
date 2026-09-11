using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Api.Endpoints;
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
public class ConsultasExternas(BancoDeTestes banco) : IDisposable
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

        var endereco = await http.GetFromJsonAsync<EnderecoDoCep>("/consultas/cep/01310-100");

        Assert.Equal("Avenida Paulista", endereco!.Logradouro);
        Assert.Equal("São Paulo", endereco.Cidade);
        Assert.Equal("SP", endereco.Uf);
    }

    [Fact]
    public async Task Cep_inexistente_devolve_404()
    {
        var aplicacao = ComConsulta(new RespostaDoCep(ResultadoDaConsulta.NaoEncontrado));
        var http = await Contas.Entrar(aplicacao, await Contas.Criar(banco, aplicacao));

        var resposta = await http.GetAsync("/consultas/cep/99999999");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Servico_fora_do_ar_devolve_503_e_nao_404()
    {
        var aplicacao = ComConsulta(new RespostaDoCep(ResultadoDaConsulta.Indisponivel));
        var http = await Contas.Entrar(aplicacao, await Contas.Criar(banco, aplicacao));

        var resposta = await http.GetAsync("/consultas/cep/01310100");

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

        var resposta = await http.GetAsync("/consultas/cep/1234");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);

        /* Bater no serviço de fora para descobrir o que já dava para ver aqui. */
        Assert.Equal(0, consulta.Chamadas);
    }

    [Fact]
    public async Task Sem_sessao_a_consulta_e_recusada()
    {
        var aplicacao = ComConsulta(new RespostaDoCep(ResultadoDaConsulta.NaoEncontrado));

        var resposta = await aplicacao.CreateClient().GetAsync("/consultas/cep/01310100");

        /* Vale a política padrão fechada: rota nova nasce exigindo sessão. */
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    /* ------------------------------------------------------------- CNPJ */

    private AplicacaoDeTestes ComCnpj(RespostaDoCnpj resposta, out CnpjDublado dublado)
    {
        var consulta = new CnpjDublado(resposta);
        dublado = consulta;

        var aplicacao = new AplicacaoDeTestes(banco.Conexao, servicos =>
            servicos.AddSingleton<IConsultaDeCnpj>(consulta));

        _abertas.Add(aplicacao);
        return aplicacao;
    }

    [Fact]
    public async Task Cnpj_encontrado_traz_razao_social_situacao_e_endereco()
    {
        var aplicacao = ComCnpj(new RespostaDoCnpj(
            ResultadoDaConsulta.Encontrado,
            new EmpresaDoCnpj("00000000000191", "BANCO DO BRASIL SA", "DIRECAO GERAL", "ATIVA",
                "SAUN QUADRA 5", "SN", "", "ASA NORTE", "70040912", "BRASILIA", "DF")),
            out _);

        var http = await Contas.Entrar(aplicacao, await Contas.Criar(banco, aplicacao));

        var empresa = await http.GetFromJsonAsync<EmpresaDoCnpj>("/consultas/cnpj/00.000.000-0001-91");

        Assert.Equal("BANCO DO BRASIL SA", empresa!.RazaoSocial);

        /* A situação vem junto de propósito: cadastrar cliente com empresa
           baixada é erro caro, e quem digita à mão não tem como saber. */
        Assert.Equal("ATIVA", empresa.Situacao);
        Assert.Equal("DF", empresa.Uf);
    }

    [Fact]
    public async Task Cnpj_com_digito_errado_nem_chega_a_consultar()
    {
        var aplicacao = ComCnpj(
            new RespostaDoCnpj(ResultadoDaConsulta.Encontrado), out var consulta);

        var http = await Contas.Entrar(aplicacao, await Contas.Criar(banco, aplicacao));

        var resposta = await http.GetAsync("/consultas/cnpj/11222333000199");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);

        var corpo = await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>();
        Assert.Contains("verificadores", Assert.Single(corpo!.Problemas).Descricao);

        /*
         * O limite de requisições da BrasilAPI é escasso. Gastar uma com um
         * número que já se sabe errado é desperdício, e volta com a resposta
         * errada: "não existe", quando o certo é "está digitado errado".
         */
        Assert.Equal(0, consulta.Chamadas);
    }

    [Fact]
    public async Task Limite_de_requisicoes_devolve_429_e_nao_503()
    {
        var aplicacao = ComCnpj(new RespostaDoCnpj(ResultadoDaConsulta.LimiteAtingido), out _);
        var http = await Contas.Entrar(aplicacao, await Contas.Criar(banco, aplicacao));

        var resposta = await http.GetAsync("/consultas/cnpj/00000000000191");

        /*
         * São coisas diferentes e a tela diz frases diferentes: 429 é "espere
         * um pouco e tente de novo", e é o caso comum numa API pública
         * gratuita; 503 é "está fora do ar", que faz desistir do atalho.
         */
        Assert.Equal(HttpStatusCode.TooManyRequests, resposta.StatusCode);
    }

    [Fact]
    public async Task Cnpj_inexistente_devolve_404()
    {
        var aplicacao = ComCnpj(new RespostaDoCnpj(ResultadoDaConsulta.NaoEncontrado), out _);
        var http = await Contas.Entrar(aplicacao, await Contas.Criar(banco, aplicacao));

        var resposta = await http.GetAsync("/consultas/cnpj/00000000000191");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    /* ------------------------------------------------------------ dublês */

    private sealed class ConsultaDublada(RespostaDoCep resposta) : IConsultaDeCep
    {
        public int Chamadas { get; private set; }

        public Task<RespostaDoCep> BuscarAsync(string cep, CancellationToken cancelamento)
        {
            Chamadas++;
            return Task.FromResult(resposta);
        }
    }

    private sealed class CnpjDublado(RespostaDoCnpj resposta) : IConsultaDeCnpj
    {
        public int Chamadas { get; private set; }

        public Task<RespostaDoCnpj> BuscarAsync(string cnpj, CancellationToken cancelamento)
        {
            Chamadas++;
            return Task.FromResult(resposta);
        }
    }
}
