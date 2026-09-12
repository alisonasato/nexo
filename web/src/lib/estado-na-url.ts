"use client";

import { useCallback, useSyncExternalStore } from "react";

/**
 * O recorte de uma listagem, guardado na barra de endereço.
 *
 * <b>Por que na URL.</b> Busca, filtro, página e ordem em estado local morrem
 * na navegação: sair para editar um cadastro e voltar devolvia a página 1 sem
 * filtro, e não havia como mandar a alguém o link de uma lista já recortada.
 *
 * <b>Por que não o <code>useSearchParams</code> do Next.</b> Em rota
 * pré-renderizada ele obriga um limite de Suspense, e o conteúdo dentro desse
 * limite chega renderizado do servidor mas <b>não hidrata</b> — a tabela
 * aparece e nada nela responde a clique. É o tipo de defeito que passa por
 * qualquer conferência visual. Ler de <code>window.location</code> com
 * <code>useSyncExternalStore</code> resolve sem esse efeito, e o próprio React
 * cuida de o servidor e a hidratação verem os padrões.
 *
 * <b>Por que <code>replaceState</code>.</b> Filtrar não é navegar. Empilhar
 * uma entrada de histórico a cada tecla faria o botão voltar desfazer letra
 * por letra, e ainda custaria um render de rota a cada uma.
 */

const EVENTO = "nexo:url";

/**
 * Faz o histórico avisar quando alguém o mexe.
 *
 * <b>Por que mexer no objeto do navegador.</b> <code>popstate</code> só dispara
 * quando o usuário navega no histórico — nunca quando alguém chama
 * <code>pushState</code>. E quem chama aqui é o roteador do Next, a cada
 * transição de rota. Sem este aviso, a listagem que abre depois de uma
 * navegação lê o endereço da tela <b>anterior</b> e fica esperando um render
 * que talvez não venha; foi o que aconteceu ao voltar de um cadastro, onde a
 * correção só chegava de carona num outro render qualquer.
 *
 * A chamada original é preservada inteira: só acrescentamos o aviso depois.
 */
let historicoAvisa = false;

function garantirQueOHistoricoAvisa(): void {
  if (historicoAvisa || typeof window === "undefined") return;
  historicoAvisa = true;

  for (const nome of ["pushState", "replaceState"] as const) {
    const original = history[nome].bind(history);

    history[nome] = (...argumentos: Parameters<History["pushState"]>) => {
      original(...argumentos);

      /* O aviso sai depois, e não no meio da chamada. O roteador do Next mexe
         no histórico de dentro de um efeito de inserção, onde o React proíbe
         agendar atualização — avisar ali rendia «useInsertionEffect must not
         schedule updates» a cada troca de tela. A microtarefa espera a
         renderização terminar, ainda no mesmo quadro. */
      queueMicrotask(() => window.dispatchEvent(new Event(EVENTO)));
    };
  }
}

function assinar(ouvinte: () => void): () => void {
  garantirQueOHistoricoAvisa();

  /* `popstate` para o botão voltar; o evento próprio para as escritas, nossas
     e do roteador, que o navegador não anuncia sozinho. */
  window.addEventListener("popstate", ouvinte);
  window.addEventListener(EVENTO, ouvinte);

  return () => {
    window.removeEventListener("popstate", ouvinte);
    window.removeEventListener(EVENTO, ouvinte);
  };
}

const enderecoAgora = () => window.location.pathname + window.location.search;

/** No servidor não há URL: a listagem começa nos padrões e ajusta ao montar. */
const enderecoNoServidor = () => "";

export type ValorDeParametro = string | number | boolean | null;

/**
 * O recorte da tela em <code>caminho</code>, lido e escrito na barra de endereço.
 *
 * @param caminho O endereço desta tela, sem consulta — <code>/pessoas</code>.
 * Serve para saber se o que está na barra de endereço já é <b>desta</b> tela:
 * numa transição de rota o React monta a tela nova antes de o roteador trocar
 * o endereço, e por um render a listagem enxerga a consulta da tela anterior.
 * Ler aquilo daria uma consulta ao servidor com o recorte errado e um lampejo
 * da lista errada na tela — por isso <code>estabilizada</code>.
 */
export function useConsultaDaUrl(caminho: string) {
  const endereco = useSyncExternalStore(assinar, enderecoAgora, enderecoNoServidor);

  const divisao = endereco.indexOf("?");
  const caminhoAtual = divisao < 0 ? endereco : endereco.slice(0, divisao);
  const parametros = new URLSearchParams(divisao < 0 ? "" : endereco.slice(divisao));

  /* Sem `useCallback` de propósito: `ler` só é chamada durante o render, nunca
     entra em lista de dependências, e o compilador do React recusa memorizar
     uma função que constrói o `URLSearchParams` dentro de si. */
  const ler = (nome: string) => parametros.get(nome);

  /**
   * Grava vários parâmetros de uma vez.
   *
   * <b>Parte da URL corrente, e não do instantâneo deste render.</b> Duas
   * chamadas em sequência — digitar e trocar de página no mesmo instante — se
   * sobrescreveriam, e a segunda apagaria o que a primeira acabou de gravar.
   *
   * Valor nulo, vazio ou falso <b>remove</b> o parâmetro: o padrão não aparece
   * na barra de endereço, então a listagem limpa tem link limpo.
   */
  const gravar = useCallback((mudancas: Record<string, ValorDeParametro>) => {
    garantirQueOHistoricoAvisa();

    const proximos = new URLSearchParams(window.location.search);

    for (const [nome, valor] of Object.entries(mudancas)) {
      if (valor === null || valor === "" || valor === false) proximos.delete(nome);
      else proximos.set(nome, String(valor));
    }

    const consultaNova = proximos.toString();

    window.history.replaceState(
      null,
      "",
      consultaNova ? window.location.pathname + "?" + consultaNova : window.location.pathname,
    );
  }, []);

  return { ler, gravar, estabilizada: caminhoAtual === caminho };
}

/**
 * O nome do parâmetro que carrega o recorte de uma tela para a outra.
 *
 * Curto de propósito: viaja numa URL que já tem identificador dentro, e
 * ninguém precisa lê-lo.
 */
const RETORNO = "de";

/**
 * Pendura o recorte corrente no endereço de destino.
 *
 * <b>Por que não bastava o botão voltar.</b> Ele funciona — entrar num cadastro
 * empilha histórico de verdade. Mas <b>Cancelar</b> e o salvamento não voltam:
 * eles vão para a listagem, e ir para a listagem sem dizer qual devolvia a
 * página 1 sem filtro. Levar o recorte junto também atende quem chegou ao
 * cadastro por link direto, que não tem para onde voltar.
 */
export function useLinkComRetorno(): (destino: string) => string {
  const endereco = useSyncExternalStore(assinar, enderecoAgora, enderecoNoServidor);
  const divisao = endereco.indexOf("?");
  const consulta = divisao < 0 ? "" : endereco.slice(divisao + 1);

  return useCallback(
    (destino: string) =>
      consulta ? destino + "?" + RETORNO + "=" + encodeURIComponent(consulta) : destino,
    [consulta],
  );
}

/**
 * Para onde volta quem está num cadastro: a listagem como ele a deixou.
 *
 * Sem recorte pendurado — link direto, aba nova, favorito — devolve a listagem
 * limpa, que é o destino certo para quem nunca esteve nela.
 */
export function useRetornoDaListagem(listagem: string): string {
  const endereco = useSyncExternalStore(assinar, enderecoAgora, enderecoNoServidor);
  const divisao = endereco.indexOf("?");
  const recorte = new URLSearchParams(divisao < 0 ? "" : endereco.slice(divisao)).get(RETORNO);

  return recorte ? listagem + "?" + recorte : listagem;
}
