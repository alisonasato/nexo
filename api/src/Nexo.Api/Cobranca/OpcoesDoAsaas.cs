namespace Nexo.Api.Cobranca;

/// <summary>
/// Como falar com o Asaas, o PSP escolhido (ver DECISOES.md).
///
/// <para>
/// <b>O padrão é o sandbox, e isso é decisão e não descuido.</b> Uma
/// configuração esquecida numa máquina qualquer emite cobrança de mentira, não
/// de verdade. O caminho para produção exige escrever o endereço à mão, que é
/// o momento em que alguém pensa no que está fazendo.
/// </para>
/// <para>
/// <b>Sem chave, a integração fica desligada</b> — não quebra a aplicação.
/// Diferente do segredo do token de sessão, que derruba a subida quando falta,
/// aqui a ausência tem significado legítimo: o escritório ainda não abriu conta
/// no PSP, e continua dando baixa à mão, que é como ele opera hoje.
/// </para>
/// </summary>
public sealed class OpcoesDoAsaas
{
    public const string Secao = "Asaas";

    /// <summary>
    /// A chave de API da conta. Vem de <c>dotnet user-secrets</c> ou de
    /// variável de ambiente, <b>nunca</b> do repositório.
    /// </summary>
    public string Chave { get; set; } = string.Empty;

    public string Endereco { get; set; } = "https://api-sandbox.asaas.com/v3/";

    /// <summary>
    /// O segredo que prova que a notificação veio mesmo do Asaas.
    ///
    /// <para>
    /// Ele chega no cabeçalho <c>asaas-access-token</c>, com o mesmo valor em
    /// toda notificação. <b>Não é assinatura por mensagem</b>: não prova que
    /// aquele corpo não foi alterado nem que aquela entrega não é repetição de
    /// outra. Quem cuida de repetição é a idempotência, e por isso ela não é
    /// reforço aqui — é a única defesa contra baixa dupla.
    /// </para>
    /// <para>
    /// É um valor próprio, e não a chave de API: a documentação do Asaas pede
    /// isso explicitamente, e faz sentido — ele viaja para dentro da nossa
    /// aplicação a cada evento, enquanto a chave de API só sai daqui.
    /// </para>
    /// </summary>
    public string TokenDoWebhook { get; set; } = string.Empty;

    /// <summary>
    /// O nome que identifica esta aplicação nas requisições.
    ///
    /// O Asaas passou a exigir <c>User-Agent</c> em contas criadas a partir de
    /// junho de 2024, e a falta dele é o tipo de erro que só aparece em
    /// produção, porque a biblioteca de HTTP do .NET não manda um sozinha.
    /// </summary>
    public string Aplicacao { get; set; } = "Nexo";

    public bool Configurado => !string.IsNullOrWhiteSpace(Chave);
}
