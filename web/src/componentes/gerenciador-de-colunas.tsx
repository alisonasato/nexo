"use client";

import { useEffect, useRef, useState } from "react";

import { Botao } from "@/componentes/controles";
import { IconeDeOrdenacao } from "@/componentes/icones";
import {
  colunasDisponiveis,
  type EscolhaDeColuna,
} from "@/app/(app)/pessoas/colunas";

type Props = {
  escolhas: EscolhaDeColuna[];
  aoAplicar: (escolhas: EscolhaDeColuna[]) => void;
  aoFechar: () => void;
};

/**
 * Quais colunas aparecem, e em que ordem.
 *
 * <b>Trabalha sobre um rascunho.</b> Marcar, desmarcar e mover não tocam na
 * tabela: quem confirma é o Aplicar, e o Cancelar joga fora tudo o que foi
 * mexido. Sem isso, quem abriu para dar uma olhada sairia com a tela
 * reconfigurada sem ter decidido nada.
 */
export function GerenciadorDeColunas({ escolhas, aoAplicar, aoFechar }: Props) {
  const [rascunho, definirRascunho] = useState<EscolhaDeColuna[]>(escolhas);
  const painel = useRef<HTMLDivElement>(null);

  /*
   * Esc fecha e clique fora fecha, e os dois cancelam. É o que a tecla e o
   * clique já significam em qualquer painel: sair sem decidir. Fazer um deles
   * aplicar seria aplicar sem ninguém ter pedido.
   */
  useEffect(() => {
    function aoTeclar(evento: KeyboardEvent) {
      if (evento.key === "Escape") aoFechar();
    }

    function aoClicar(evento: MouseEvent) {
      if (!painel.current?.contains(evento.target as Node)) aoFechar();
    }

    document.addEventListener("keydown", aoTeclar);
    /* Na captura: um clique em botão que some do DOM não chegaria à fase de bolha. */
    document.addEventListener("mousedown", aoClicar, true);

    return () => {
      document.removeEventListener("keydown", aoTeclar);
      document.removeEventListener("mousedown", aoClicar, true);
    };
  }, [aoFechar]);

  const titulos = new Map(colunasDisponiveis.map((coluna) => [coluna.chave, coluna.titulo]));
  const visiveis = rascunho.filter((escolha) => escolha.visivel).length;

  function alternar(chave: string) {
    definirRascunho((atual) =>
      atual.map((escolha) =>
        escolha.chave === chave ? { ...escolha, visivel: !escolha.visivel } : escolha,
      ),
    );
  }

  function mover(indice: number, passo: -1 | 1) {
    const destino = indice + passo;
    if (destino < 0 || destino >= rascunho.length) return;

    definirRascunho((atual) => {
      const copia = [...atual];
      [copia[indice], copia[destino]] = [copia[destino], copia[indice]];
      return copia;
    });
  }

  return (
    <div
      ref={painel}
      role="dialog"
      aria-modal="true"
      aria-label="Colunas da listagem"
      /*
        No celular ele é painel preso às bordas; a partir de 640px vira o menu
        pendurado no botão. Largura fixa nos dois casos deixava 65px dele fora
        da tela num aparelho de 375px — e o que sobrava de fora eram as caixas
        de marcação.
      */
      className="fixed inset-x-4 top-24 z-20 rounded-[--radius-cartao] border border-borda bg-superficie p-4 shadow-nivel-2 sm:absolute sm:inset-x-auto sm:top-full sm:right-0 sm:mt-2 sm:w-80"
    >
      <p className="font-semibold text-marca-950">Colunas</p>
      <p className="mt-1 text-slate-500">
        Marque o que aparece e use as setas para mudar a ordem.
      </p>

      <ul className="mt-3 flex flex-col gap-1">
        {rascunho.map((escolha, indice) => (
          <li key={escolha.chave} className="flex items-center gap-2 rounded-[--radius-controle] px-1 py-1 hover:bg-slate-50">
            <label className="flex flex-1 items-center gap-2 text-slate-700">
              <input
                type="checkbox"
                checked={escolha.visivel}
                onChange={() => alternar(escolha.chave)}
                className="size-4 rounded border-borda-forte accent-marca-600"
              />
              {titulos.get(escolha.chave)}
            </label>

            <button
              type="button"
              onClick={() => mover(indice, -1)}
              disabled={indice === 0}
              aria-label={`Subir ${titulos.get(escolha.chave)}`}
              className="inline-flex size-7 items-center justify-center rounded border border-borda-forte text-slate-600 transition-colors hover:bg-slate-100 disabled:cursor-not-allowed disabled:text-slate-300"
            >
              <IconeDeOrdenacao estado="crescente" />
            </button>

            <button
              type="button"
              onClick={() => mover(indice, 1)}
              disabled={indice === rascunho.length - 1}
              aria-label={`Descer ${titulos.get(escolha.chave)}`}
              className="inline-flex size-7 items-center justify-center rounded border border-borda-forte text-slate-600 transition-colors hover:bg-slate-100 disabled:cursor-not-allowed disabled:text-slate-300"
            >
              <IconeDeOrdenacao estado="decrescente" />
            </button>
          </li>
        ))}
      </ul>

      {/*
        Esconder tudo deixaria uma tabela de cabeçalho vazio e nenhum caminho de
        volta a não ser reabrir este painel. Barrar aqui custa uma linha; sair
        daquele estado custa a paciência de quem caiu nele.
      */}
      {visiveis === 0 && (
        <p role="alert" className="mt-3 text-amber-800">
          Deixe ao menos uma coluna marcada.
        </p>
      )}

      <div className="mt-4 flex justify-end gap-2">
        <Botao aparencia="secundario" type="button" onClick={aoFechar} className="px-3 py-1.5">
          Cancelar
        </Botao>

        <Botao
          type="button"
          disabled={visiveis === 0}
          onClick={() => aoAplicar(rascunho)}
          className="px-3 py-1.5"
        >
          Aplicar
        </Botao>
      </div>
    </div>
  );
}
