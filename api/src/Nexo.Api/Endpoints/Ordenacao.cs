namespace Nexo.Api.Endpoints;

/// <summary>
/// Para que lado a listagem é ordenada.
///
/// <para>
/// <b>A ordem é decidida no banco, nunca na tela.</b> Ordenar no navegador
/// ordenaria a página, não a lista: com 25 de 300, o resultado seria uma ordem
/// perfeita dentro de um recorte arbitrário, e a página 2 traria linhas que
/// deviam vir antes das da página 1. Plausível e errado, que é o jeito mais
/// caro de errar. É a mesma razão pela qual nenhum total desta aplicação é
/// somado na tela.
/// </para>
/// <para>
/// <b>Toda ordenação termina no identificador.</b> Sem um critério que nunca
/// empata, duas linhas de mesmo valor não têm posição definida entre si — e o
/// banco pode devolvê-las em ordem diferente a cada consulta. Paginando, isso
/// não embaralha: faz uma linha aparecer em duas páginas e outra em nenhuma.
/// Ordenar por valor, onde o empate é a regra e não a exceção, transforma esse
/// detalhe em cobrança que some da lista.
/// </para>
/// </summary>
public enum Direcao
{
    Crescente = 1,
    Decrescente = 2,
}
