import type { ReactNode } from "react";

import type { components } from "@/api/esquema";
import { formatarDocumento, formatarEndereco, formatarTelefone } from "@/lib/formato";

type PessoaNaLista = components["schemas"]["PessoaNaLista"];

export type Coluna = {
  chave: string;
  titulo: string;
  /** Fora da configuração guardada, é o que a tela mostra na primeira visita. */
  padrao: boolean;
  /** Classes da célula. Números tabulares alinham coluna de dígitos. */
  classe?: string;
  /**
   * Texto à esquerda; identificador, não.
   *
   * Regra de tipografia de tabela: o olho lê texto pela margem esquerda, e
   * compara número pela direita. Código é número curto de largura variável, e
   * alinhado à direita as unidades ficam na mesma coluna. CPF/CNPJ tem sempre a
   * mesma largura depois de formatado, então centralizar forma um bloco
   * regular que se varre de cima a baixo sem tropeço.
   */
  alinhamento?: "esquerda" | "centro" | "direita";
  /**
   * O nome pelo qual a API ordena esta coluna, quando ela pode ser ordenada.
   *
   * Só existe para as colunas em que o banco sabe ordenar. Deixar a tela
   * oferecer ordenação de uma coluna que a API não entende produziria um
   * cabeçalho que responde ao clique e não muda nada.
   */
  ordenarPor?: "Nome" | "Codigo";
  conteudo: (pessoa: PessoaNaLista) => ReactNode;
};

export const classeDeAlinhamento = (coluna: Coluna): string =>
  coluna.alinhamento === "direita"
    ? "text-right"
    : coluna.alinhamento === "centro"
      ? "text-center"
      : "text-left";

/**
 * As colunas que a listagem sabe mostrar, na ordem em que aparecem sem
 * configuração nenhuma.
 *
 * <b>A ordem daqui é o padrão, não a regra.</b> Quem manda é o que a pessoa
 * guardou; esta lista existe para dizer o que existe, o que aparece na
 * primeira visita, e o que fazer quando uma coluna nova surge depois de
 * alguém já ter salvo a configuração dele.
 */
export const colunasDisponiveis: Coluna[] = [
  {
    chave: "codigo",
    titulo: "Código",
    padrao: true,
    alinhamento: "direita",
    ordenarPor: "Codigo",
    classe: "numeros-tabulares font-medium text-slate-800",
    conteudo: (pessoa) => pessoa.codigo,
  },
  {
    chave: "nome",
    titulo: "Nome",
    padrao: true,
    ordenarPor: "Nome",
    conteudo: (pessoa) => (
      <>
        {pessoa.nome}
        {pessoa.nomeFantasia && (
          <span className="block text-slate-500">{pessoa.nomeFantasia}</span>
        )}
      </>
    ),
  },
  {
    chave: "documento",
    titulo: "CPF/CNPJ",
    padrao: true,
    alinhamento: "centro",
    classe: "numeros-tabulares text-slate-700",
    conteudo: (pessoa) => formatarDocumento(pessoa.documento) || "—",
  },
  {
    chave: "endereco",
    titulo: "Endereço",
    padrao: true,
    classe: "text-slate-700",
    conteudo: (pessoa) => formatarEndereco(pessoa.endereco) || "—",
  },
  {
    chave: "contato",
    titulo: "Contato",
    padrao: true,
    classe: "numeros-tabulares text-slate-700",
    /* O fixo na frente do celular, e o celular quando não há fixo. */
    conteudo: (pessoa) => formatarTelefone(pessoa.telefone || pessoa.celular) || "—",
  },
  {
    chave: "email",
    titulo: "E-mail",
    padrao: false,
    classe: "text-slate-700",
    conteudo: (pessoa) => pessoa.email || "—",
  },
];

/** Chave e visibilidade, que é tudo o que a configuração guarda. */
export type EscolhaDeColuna = { chave: string; visivel: boolean };

const CHAVE_GUARDADA = "nexo.pessoas.colunas";

export const escolhasPadrao = (): EscolhaDeColuna[] =>
  colunasDisponiveis.map((coluna) => ({ chave: coluna.chave, visivel: coluna.padrao }));

/**
 * Junta o que foi guardado com o que o código conhece hoje.
 *
 * <b>É aqui que uma configuração antiga não vira tela quebrada.</b> Coluna
 * que o código não conhece mais é descartada; coluna nova, que não existia
 * quando a pessoa salvou, entra no fim com a visibilidade padrão. Sem esta
 * reconciliação, acrescentar uma coluna no futuro faria ela nunca aparecer
 * para quem já tinha salvo — e remover uma faria a tela tentar desenhar o que
 * não existe.
 */
export function reconciliar(guardadas: EscolhaDeColuna[]): EscolhaDeColuna[] {
  const conhecidas = new Set(colunasDisponiveis.map((coluna) => coluna.chave));

  const mantidas = guardadas.filter((escolha) => conhecidas.has(escolha.chave));
  const jaVistas = new Set(mantidas.map((escolha) => escolha.chave));

  const novas = colunasDisponiveis
    .filter((coluna) => !jaVistas.has(coluna.chave))
    .map((coluna) => ({ chave: coluna.chave, visivel: coluna.padrao }));

  return [...mantidas, ...novas];
}

/**
 * O que está guardado neste navegador, ou o padrão.
 *
 * Tudo o que vem do `localStorage` é tratado como suspeito: pode não existir,
 * pode ser de uma versão anterior, pode ter sido editado à mão, e o próprio
 * acesso pode lançar — janela anônima, site bloqueado, captura de miniatura.
 * Qualquer um desses casos cai no padrão, que é uma tela funcionando.
 */
export function lerEscolhas(): EscolhaDeColuna[] {
  if (typeof window === "undefined") return escolhasPadrao();

  try {
    const bruto = window.localStorage.getItem(CHAVE_GUARDADA);
    if (!bruto) return escolhasPadrao();

    const lido: unknown = JSON.parse(bruto);
    if (!Array.isArray(lido)) return escolhasPadrao();

    const escolhas = lido
      .filter(
        (item): item is EscolhaDeColuna =>
          typeof item === "object" &&
          item !== null &&
          typeof (item as EscolhaDeColuna).chave === "string" &&
          typeof (item as EscolhaDeColuna).visivel === "boolean",
      )
      .map((item) => ({ chave: item.chave, visivel: item.visivel }));

    return reconciliar(escolhas);
  } catch {
    return escolhasPadrao();
  }
}

/* ------------------------------------------------- a configuração em uso */

/*
 * O valor devolvido durante a renderização no servidor e na hidratação.
 *
 * É uma constante de módulo, e não uma chamada: `useSyncExternalStore` compara
 * por identidade, e devolver um array novo a cada leitura faria o React
 * renderizar para sempre.
 */
const PADRAO_ESTAVEL: EscolhaDeColuna[] = escolhasPadrao();

let emUso: EscolhaDeColuna[] | null = null;
const ouvintes = new Set<() => void>();

/**
 * A configuração de agora, lida do navegador na primeira vez e guardada em
 * memória depois.
 *
 * <b>Não é um `useEffect` copiando o `localStorage` para o estado</b>, e a
 * diferença não é de gosto. O efeito roda depois da primeira pintura, então a
 * tabela apareceria com as colunas padrão e se reorganizaria na frente de quem
 * está olhando. Com `useSyncExternalStore`, o React usa o padrão no servidor e
 * na hidratação e troca para o valor real sem passo intermediário visível.
 */
export function escolhasEmUso(): EscolhaDeColuna[] {
  if (emUso === null) emUso = lerEscolhas();
  return emUso;
}

export const escolhasNoServidor = (): EscolhaDeColuna[] => PADRAO_ESTAVEL;

export function assinarEscolhas(ouvinte: () => void): () => void {
  ouvintes.add(ouvinte);
  return () => {
    ouvintes.delete(ouvinte);
  };
}

/** Guardar é conveniência: falhar aqui não pode derrubar a tela. */
export function gravarEscolhas(escolhas: EscolhaDeColuna[]): void {
  emUso = escolhas;

  try {
    window.localStorage.setItem(CHAVE_GUARDADA, JSON.stringify(escolhas));
  } catch {
    /* Janela anônima, cota cheia, site bloqueado: a tela segue com o que está na memória. */
  }

  ouvintes.forEach((ouvinte) => ouvinte());
}

/** As colunas visíveis, na ordem escolhida. */
export function colunasVisiveis(escolhas: EscolhaDeColuna[]): Coluna[] {
  const porChave = new Map(colunasDisponiveis.map((coluna) => [coluna.chave, coluna]));

  return escolhas
    .filter((escolha) => escolha.visivel)
    .map((escolha) => porChave.get(escolha.chave))
    .filter((coluna): coluna is Coluna => coluna !== undefined);
}
