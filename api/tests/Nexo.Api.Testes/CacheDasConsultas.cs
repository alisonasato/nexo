using Microsoft.Extensions.Caching.Memory;
using Nexo.Api.Servicos;

namespace Nexo.Api.Testes;

/// <summary>
/// O cache das consultas a serviços de fora.
///
/// <para>
/// Não precisa de banco nem de HTTP: o que está em prova é <b>o que se guarda
/// e o que não se guarda</b>, e essa é a decisão inteira.
/// </para>
/// <para>
/// A regra que mais importa é a negativa. Guardar uma falha transformaria um
/// tropeço de dez segundos numa hora sem o atalho — o oposto do que o cache
/// existe para fazer, e um defeito que ninguém associaria ao cache.
/// </para>
/// </summary>
public class CacheDasConsultas
{
    private static IMemoryCache Novo() =>
        new MemoryCache(new MemoryCacheOptions { SizeLimit = Cache.Entradas });

    private sealed class CepDublado(RespostaDoCep resposta) : IConsultaDeCep
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

    [Fact]
    public async Task Endereco_encontrado_nao_e_perguntado_duas_vezes()
    {
        var interna = new CepDublado(new RespostaDoCep(
            ResultadoDaConsulta.Encontrado,
            new EnderecoDoCep("01310100", "Avenida Paulista", "Bela Vista", "São Paulo", "SP")));

        var consulta = new ConsultaDeCepComCache(interna, Novo());

        var primeira = await consulta.BuscarAsync("01310100", default);
        var segunda = await consulta.BuscarAsync("01310-100", default);

        Assert.Equal(1, interna.Chamadas);

        /* A pontuação não cria uma entrada nova: a chave é só o número. */
        Assert.Equal(primeira.Endereco!.Logradouro, segunda.Endereco!.Logradouro);
    }

    [Fact]
    public async Task Servico_fora_do_ar_nunca_fica_guardado()
    {
        var interna = new CepDublado(new RespostaDoCep(ResultadoDaConsulta.Indisponivel));
        var consulta = new ConsultaDeCepComCache(interna, Novo());

        await consulta.BuscarAsync("01310100", default);
        await consulta.BuscarAsync("01310100", default);
        await consulta.BuscarAsync("01310100", default);

        /*
         * Três perguntas, três idas. Guardar a falha faria o atalho continuar
         * quebrado depois de o serviço voltar, e ninguém ligaria uma coisa à
         * outra.
         */
        Assert.Equal(3, interna.Chamadas);
    }

    [Fact]
    public async Task Limite_de_requisicoes_tambem_nao_fica_guardado()
    {
        var interna = new CnpjDublado(new RespostaDoCnpj(ResultadoDaConsulta.LimiteAtingido));
        var consulta = new ConsultaDeCnpjComCache(interna, Novo());

        await consulta.BuscarAsync("00000000000191", default);
        await consulta.BuscarAsync("00000000000191", default);

        /* "Espere um pouco" guardado vira "espere doze horas". */
        Assert.Equal(2, interna.Chamadas);
    }

    [Fact]
    public async Task Empresa_encontrada_e_guardada()
    {
        var interna = new CnpjDublado(new RespostaDoCnpj(
            ResultadoDaConsulta.Encontrado,
            new EmpresaDoCnpj("00000000000191", "BANCO DO BRASIL SA", "", "ATIVA",
                "SAUN QUADRA 5", "SN", "", "ASA NORTE", "70040912", "BRASILIA", "DF")));

        var consulta = new ConsultaDeCnpjComCache(interna, Novo());

        await consulta.BuscarAsync("00000000000191", default);
        var segunda = await consulta.BuscarAsync("00.000.000-0001-91", default);

        Assert.Equal(1, interna.Chamadas);
        Assert.Equal("BANCO DO BRASIL SA", segunda.Empresa!.RazaoSocial);
    }

    [Fact]
    public void Nao_encontrado_dura_muito_menos_que_encontrado()
    {
        /*
         * "Não existe" quase sempre é erro de digitação, e o conserto vem
         * segundos depois. Guardar pelo mesmo tempo de uma resposta boa faria
         * o segundo acerto continuar falhando.
         */
        Assert.True(Cache.Duracao(ResultadoDaConsulta.NaoEncontrado)
                  < Cache.Duracao(ResultadoDaConsulta.Encontrado));

        Assert.Null(Cache.Duracao(ResultadoDaConsulta.Indisponivel));
        Assert.Null(Cache.Duracao(ResultadoDaConsulta.LimiteAtingido));
    }
}
