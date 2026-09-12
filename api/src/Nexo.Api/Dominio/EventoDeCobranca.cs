namespace Nexo.Api.Dominio;

/// <summary>
/// Um aviso do PSP que já foi processado.
///
/// <para>
/// <b>Esta tabela existe por uma linha da documentação do Asaas:</b> a entrega
/// é <i>at least once</i>, então o mesmo evento chega mais de uma vez. Sem
/// registro do que já passou, um reenvio de "pagamento recebido" daria baixa
/// duas vezes — e baixa dupla é o mesmo estrago que cobrança dupla, só que do
/// outro lado do caixa.
/// </para>
/// <para>
/// <b>A chave é o identificador do evento, não um id nosso.</b> É o que o
/// Asaas manda em <c>id</c>, e é o único valor que identifica <i>aquela
/// entrega</i>. Chave própria com índice único sobre o id deles daria no
/// mesmo com um passo a mais; chave deles deixa o banco recusar a repetição
/// na inserção, que é onde a corrida acontece — duas entregas simultâneas do
/// mesmo evento são exatamente o caso que uma checagem no código perde.
/// </para>
/// <para>
/// É o mesmo desenho do índice único que impede gerar a mensalidade duas
/// vezes. A regra do projeto se repete porque o problema se repete: quando
/// errar custa dinheiro, quem recusa é o banco.
/// </para>
/// </summary>
public class EventoDeCobranca
{
    /// <summary>O <c>id</c> do evento no Asaas — <c>evt_...</c>.</summary>
    public string Id { get; set; } = string.Empty;

    public Guid TenantId { get; set; }

    /// <summary>O tipo, como <c>PAYMENT_RECEIVED</c>. Guardado para leitura humana.</summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>
    /// O recebível que o evento atingiu, se atingiu algum.
    ///
    /// Nulo quando o evento foi reconhecido e ignorado — cobrança visualizada,
    /// boleto impresso. Guardar mesmo assim é o que impede reprocessar, e o
    /// que deixa o histórico dizer que o aviso chegou.
    /// </summary>
    public Guid? RecebivelId { get; set; }

    public DateTimeOffset RecebidoEm { get; set; }
}
