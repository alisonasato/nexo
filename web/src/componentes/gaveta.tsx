"use client";

import { useEffect, useId, useRef, type ReactNode } from "react";

import { IconeDeFechar } from "@/componentes/icones";

type Props = {
  titulo: string;
  descricao?: string;
  aberta: boolean;
  aoFechar: () => void;
  children: ReactNode;
  /** Preso embaixo: é onde mora o botão de confirmar, sempre à vista. */
  rodape?: ReactNode;
};

/**
 * Um painel que entra pela direita, por cima da tela, sem tirar ninguém dela.
 *
 * <p>
 * <b>Gaveta, e não página.</b> Lançar, renegociar e baixar em lote são coisas
 * que se faz olhando a lista: a tabela continua ali atrás, e fechar devolve a
 * pessoa exatamente onde estava, com o recorte e a seleção intactos.
 * </p>
 * <p>
 * <b>É um <c>dialog</c> de verdade.</b> Foco preso dentro, Tab dando a volta,
 * Escape fechando, fundo inerte, véu e devolução do foco ao botão de origem são
 * do próprio elemento. A versão anterior fazia tudo isso à mão, com uma lista de
 * seletores focáveis que envelhece a cada controle novo.
 * </p>
 * <p>
 * Quem manda continua sendo o estado de quem abriu: Escape e clique no véu
 * pedem para fechar, e o fechamento acontece quando o estado muda.
 * </p>
 */
export function Gaveta({ titulo, descricao, aberta, aoFechar, children, rodape }: Props) {
  const painel = useRef<HTMLDialogElement>(null);
  const idDoTitulo = useId();
  const idDaDescricao = useId();

  /*
   * O fechar vive numa referência porque quem usa a gaveta passa uma função nova
   * a cada render, e ela não pode entrar nas dependências do efeito que abre.
   */
  const fechar = useRef(aoFechar);
  useEffect(() => {
    fechar.current = aoFechar;
  });

  useEffect(() => {
    const dialogo = painel.current;
    if (!dialogo) return;

    if (aberta && !dialogo.open) dialogo.showModal();
    if (!aberta && dialogo.open) dialogo.close();
  }, [aberta]);

  return (
    <dialog
      ref={painel}
      aria-labelledby={idDoTitulo}
      aria-describedby={descricao ? idDaDescricao : undefined}
      /* Escape pede o fechamento; quem fecha é o estado, senão a gaveta some com ele dizendo que está aberta. */
      onCancel={(evento) => {
        evento.preventDefault();
        fechar.current();
      }}
      /*
       * A mesma porta, pela tecla. O `cancel` é do próprio elemento, mas nem todo
       * navegador embutido o dispara — no que esta gaveta foi conferida, não
       * dispara —, e fechar com Escape é básico demais para depender disso. As
       * duas portas levam ao mesmo lugar, e pedir para fechar duas vezes não muda
       * nada.
       */
      onKeyDown={(evento) => {
        if (evento.key !== "Escape") return;
        evento.preventDefault();
        fechar.current();
      }}
      /* Clique no véu chega com o próprio dialog como alvo: o painel cobre todo o resto. */
      onClick={(evento) => {
        if (evento.target === painel.current) fechar.current();
      }}
      className="m-0 ml-auto h-dvh max-h-none w-full max-w-md border-0 bg-transparent p-0 backdrop:bg-slate-900/40"
    >
      <div className="flex h-full flex-col bg-superficie shadow-nivel-3 motion-safe:animate-[entrar-da-direita_200ms_ease-out]">
        <header className="flex items-start justify-between gap-4 border-b border-borda px-5 py-4">
          <div className="min-w-0">
            {/* Recebe o foco ao abrir, para o leitor de tela anunciar onde se está; não é controle, e não leva anel. */}
            <h2
              id={idDoTitulo}
              autoFocus
              tabIndex={-1}
              className="text-lg font-semibold text-marca-950 outline-none"
            >
              {titulo}
            </h2>
            {descricao && (
              <p id={idDaDescricao} className="mt-0.5 text-slate-600">
                {descricao}
              </p>
            )}
          </div>

          <button
            type="button"
            onClick={() => fechar.current()}
            aria-label="Fechar"
            className="inline-flex size-11 shrink-0 items-center justify-center rounded-[--radius-controle] text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-800"
          >
            <IconeDeFechar className="size-5" />
          </button>
        </header>

        <div className="flex-1 overflow-y-auto px-5 py-4">{children}</div>

        {rodape && <footer className="border-t border-borda px-5 py-4">{rodape}</footer>}
      </div>
    </dialog>
  );
}
