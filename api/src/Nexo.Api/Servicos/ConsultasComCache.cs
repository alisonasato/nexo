using Microsoft.Extensions.Caching.Memory;
using Nexo.Api.Dominio;

namespace Nexo.Api.Servicos;

/// <summary>
/// Guarda por um tempo o que já foi perguntado lá fora.
///
/// <para>
/// <b>O que se guarda e o que não se guarda é a decisão inteira.</b> Resposta
/// encontrada dura horas, porque endereço de CEP e razão social de CNPJ quase
/// não mudam. Resposta "não existe" dura minutos, porque quase sempre é erro de
/// digitação, e guardar por muito tempo faria o segundo acerto continuar
/// falhando.
/// </para>
/// <para>
/// <b>Falha não se guarda nunca.</b> Serviço fora do ar e limite de requisições
/// são estados passageiros, e guardá-los transformaria um tropeço de dez
/// segundos numa hora sem o atalho — exatamente o contrário do que o cache é
/// para fazer.
/// </para>
/// <para>
/// A memória é do processo, e some quando o contêiner reinicia. É o bastante:
/// o que se quer evitar é o escritório cadastrar cinco clientes do mesmo prédio
/// numa tarde e esbarrar no limite da BrasilAPI no terceiro.
/// </para>
/// </summary>
public static class Cache
{
    /// <summary>Quanto tempo uma resposta boa vale.</summary>
    public static readonly TimeSpan Encontrado = TimeSpan.FromHours(12);

    /// <summary>
    /// Quanto tempo um "não existe" vale. Curto: quase sempre é erro de
    /// digitação, e o conserto vem segundos depois.
    /// </summary>
    public static readonly TimeSpan NaoEncontrado = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Teto de entradas. Cada uma é pequena, mas sem limite a memória cresce
    /// com o número de consultas diferentes e nunca devolve nada.
    /// </summary>
    public const int Entradas = 5_000;

    public static TimeSpan? Duracao(ResultadoDaConsulta resultado) => resultado switch
    {
        ResultadoDaConsulta.Encontrado => Encontrado,
        ResultadoDaConsulta.NaoEncontrado => NaoEncontrado,

        /* Indisponível e limite atingido: passageiros, não se guardam. */
        _ => null,
    };
}

public sealed class ConsultaDeCepComCache(IConsultaDeCep interna, IMemoryCache cache) : IConsultaDeCep
{
    public async Task<RespostaDoCep> BuscarAsync(string cep, CancellationToken cancelamento)
    {
        var chave = "cep:" + Documento.ApenasDigitos(cep);

        if (cache.TryGetValue(chave, out RespostaDoCep? guardada) && guardada is not null)
            return guardada;

        var resposta = await interna.BuscarAsync(cep, cancelamento);

        if (Cache.Duracao(resposta.Resultado) is { } duracao)
            cache.Set(chave, resposta, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = duracao, Size = 1 });

        return resposta;
    }
}

public sealed class ConsultaDeCnpjComCache(IConsultaDeCnpj interna, IMemoryCache cache) : IConsultaDeCnpj
{
    public async Task<RespostaDoCnpj> BuscarAsync(string cnpj, CancellationToken cancelamento)
    {
        var chave = "cnpj:" + Documento.ApenasDigitos(cnpj);

        if (cache.TryGetValue(chave, out RespostaDoCnpj? guardada) && guardada is not null)
            return guardada;

        var resposta = await interna.BuscarAsync(cnpj, cancelamento);

        if (Cache.Duracao(resposta.Resultado) is { } duracao)
            cache.Set(chave, resposta, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = duracao, Size = 1 });

        return resposta;
    }
}
