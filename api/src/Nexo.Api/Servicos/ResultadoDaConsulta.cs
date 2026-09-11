namespace Nexo.Api.Servicos;

/// <summary>
/// O desfecho de uma consulta a serviço de fora.
///
/// <para>
/// São quatro e não dois porque a tela precisa dizer quatro coisas diferentes.
/// Achatar tudo em "deu erro" manda a pessoa conferir um número que estava
/// certo, ou desistir de um atalho que voltaria a funcionar em dez segundos.
/// </para>
/// </summary>
public enum ResultadoDaConsulta
{
    Encontrado = 1,

    /// <summary>O serviço respondeu, e disse que isto não existe.</summary>
    NaoEncontrado = 2,

    /// <summary>
    /// Não deu para perguntar: fora do ar, lento demais, sem internet.
    /// </summary>
    Indisponivel = 3,

    /// <summary>
    /// O serviço pediu para esperar. Acontece com API pública e gratuita, e é
    /// diferente de estar fora do ar: a próxima tentativa daqui a pouco
    /// funciona, e a pessoa precisa saber disso em vez de achar que quebrou.
    /// </summary>
    LimiteAtingido = 4,
}
