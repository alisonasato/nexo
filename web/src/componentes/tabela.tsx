"use client";

import { IconeDeOrdenacao } from "@/componentes/icones";
import type { ValorDeParametro } from "@/lib/estado-na-url";

/**
 * O que três listagens fazem igual.
 *
 * <b>Por que existe agora, e não antes.</b> A regra escrita no DEPOIS.md é
 * extrair o que se repete quando a segunda ou terceira tela mostrar a
 * repetição — não antes, para não inventar abstração a partir de um caso só.
 * Pessoas, contratos e recebíveis ordenam pelos mesmos gestos: clicar no
 * cabeçalho ordena por ele, clicar de novo inverte, e a escolha mora na URL.
 * Três é a conta.
 */

export type Alinhamento = "esquerda" | "centro" | "direita";
export type Direcao = "Crescente" | "Decrescente";

export const classeDeAlinhamento = (alinhamento?: Alinhamento): string =>
  alinhamento === "direita" ? "text-right" : alinhamento === "centro" ? "text-center" : "text-left";

const classeDeJustificacao = (alinhamento?: Alinhamento): string =>
  alinhamento === "direita"
    ? "justify-end"
    : alinhamento === "centro"
      ? "justify-center"
      : "justify-start";

type Consulta = {
  ler: (nome: string) => string | null;
  gravar: (mudancas: Record<string, ValorDeParametro>) => void;
};

export type Ordem<T extends string> = { por: T; direcao: Direcao };

/**
 * A ordem da listagem, lida e escrita na barra de endereço.
 *
 * <p>
 * <b>Coluna nova começa sempre crescente.</b> Herdar a direção da coluna
 * anterior faria o primeiro clique num cabeçalho devolver a ordem de trás para
 * a frente, que ninguém pede ao clicar pela primeira vez.
 * </p>
 * <p>
 * <b>O padrão não vai para a URL.</b> Uma listagem sem nada escolhido tem link
 * limpo, e quem recebe o link não herda uma escolha que ninguém fez.
 * </p>
 *
 * @param padrao A coluna pela qual a API ordena quando ninguém pede nada. Tem
 * de ser a mesma do servidor: divergir faria a primeira página chegar numa
 * ordem e a seta apontar para outra.
 */
export function useOrdenacao<T extends string>(consulta: Consulta, padrao: T) {
  const por = (consulta.ler("ordem") ?? padrao) as T;
  const direcao = (consulta.ler("direcao") ?? "Crescente") as Direcao;

  const ordenarPor = (nova: T) => {
    const inverter = por === nova && direcao === "Crescente";

    consulta.gravar({
      ordem: nova === padrao ? null : nova,
      direcao: inverter ? "Decrescente" : null,
      /* A página 3 da ordem antiga não é a página 3 da nova. */
      pagina: null,
    });
  };

  return { por, direcao, ordenarPor };
}

type PropsDoCabecalho<T extends string> = {
  titulo: string;
  alinhamento?: Alinhamento;
  /**
   * Por qual nome a API ordena esta coluna. Ausente, o cabeçalho é só texto.
   *
   * Oferecer ordenação de uma coluna que a API não entende produziria um
   * cabeçalho que responde ao clique e não muda nada.
   */
  por?: T;
  ordem: Ordem<T>;
  aoOrdenar: (por: T) => void;
  className?: string;
};

/** Uma célula de cabeçalho que ordena ao ser clicada, quando sabe ordenar. */
export function CabecalhoOrdenavel<T extends string>({
  titulo,
  alinhamento,
  por,
  ordem,
  aoOrdenar,
  className = "",
}: PropsDoCabecalho<T>) {
  const ativa = por !== undefined && por === ordem.por;

  return (
    <th
      scope="col"
      /*
        `aria-sort` é o que faz um leitor de tela anunciar "ordenado de forma
        crescente" ao chegar na coluna. Sem ele, a seta é informação só para
        quem enxerga.
      */
      aria-sort={
        !ativa ? undefined : ordem.direcao === "Crescente" ? "ascending" : "descending"
      }
      className={`font-semibold ${classeDeAlinhamento(alinhamento)} ${por ? "p-0" : "px-4 py-3"} ${className}`}
    >
      {por ? (
        <button
          type="button"
          onClick={() => aoOrdenar(por)}
          className={
            "inline-flex w-full cursor-pointer items-center gap-1.5 px-4 py-3 text-xs tracking-wide uppercase transition-colors hover:bg-slate-100 " +
            classeDeJustificacao(alinhamento) +
            (ativa ? " text-slate-800" : "")
          }
        >
          {titulo}
          {/*
            O ícone ocupa o mesmo espaço nos três estados, então ordenar não faz
            o cabeçalho pular de largura. Inativo, ele fica pálido — convite,
            não informação.
          */}
          <IconeDeOrdenacao
            estado={!ativa ? "neutro" : ordem.direcao === "Crescente" ? "crescente" : "decrescente"}
            className={ativa ? "size-3.5 text-marca-600" : "size-3.5 text-slate-300"}
          />
        </button>
      ) : (
        <span className="block px-4 py-3">{titulo}</span>
      )}
    </th>
  );
}
