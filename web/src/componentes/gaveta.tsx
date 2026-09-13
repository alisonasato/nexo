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

const FOCAVEIS =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Um painel que entra pela direita, por cima da tela, sem tirar ninguém dela.
 *
 * <p>
 * <b>Gaveta, e não página.</b> Lançar, renegociar e baixar em lote são coisas
 * que se faz olhando a lista: a tabela continua ali atrás, e fechar devolve a
 * pessoa exatamente onde estava, com o recorte e a seleção intactos.
 * </p>
 * <p>
 * <b>Comporta-se como diálogo de verdade.</b> O foco entra no título ao abrir,
 * para o leitor de tela anunciar onde se está; Tab dá a volta dentro da gaveta
 * em vez de escapar para a tabela escondida atrás do véu; Escape e o clique no
 * véu fecham; e ao fechar o foco volta ao botão que abriu.
 * </p>
 */
export function Gaveta({ titulo, descricao, aberta, aoFechar, children, rodape }: Props) {
  const painel = useRef<HTMLDivElement>(null);
  const cabecalho = useRef<HTMLHeadingElement>(null);
  const idDoTitulo = useId();
  const idDaDescricao = useId();

  /*
   * O fechar vive numa referência, e não nas dependências do efeito.
   *
   * Quem usa a gaveta passa uma função nova a cada render. Com ela nas
   * dependências, cada letra digitada num campo recriaria o efeito: a limpeza
   * devolveria o foco ao botão de origem e a abertura o jogaria de novo no
   * título, e digitar viraria impossível.
   */
  const fechar = useRef(aoFechar);
  useEffect(() => {
    fechar.current = aoFechar;
  });

  useEffect(() => {
    if (!aberta) return;

    const origem = document.activeElement as HTMLElement | null;
    cabecalho.current?.focus();

    function aoTeclar(evento: KeyboardEvent) {
      if (evento.key === "Escape") {
        evento.stopPropagation();
        fechar.current();
        return;
      }

      if (evento.key !== "Tab" || !painel.current) return;

      const itens = [...painel.current.querySelectorAll<HTMLElement>(FOCAVEIS)];
      if (itens.length === 0) return;

      const primeiro = itens[0];
      const ultimo = itens[itens.length - 1];
      const atual = document.activeElement;

      if (evento.shiftKey && (atual === primeiro || atual === cabecalho.current)) {
        evento.preventDefault();
        ultimo.focus();
      } else if (!evento.shiftKey && atual === ultimo) {
        evento.preventDefault();
        primeiro.focus();
      }
    }

    document.addEventListener("keydown", aoTeclar);

    return () => {
      document.removeEventListener("keydown", aoTeclar);
      origem?.focus?.();
    };
  }, [aberta]);

  if (!aberta) return null;

  return (
    <div className="fixed inset-0 z-40 flex justify-end">
      <div
        aria-hidden="true"
        onClick={() => fechar.current()}
        className="absolute inset-0 bg-slate-900/40 motion-safe:animate-[aparecer_150ms_ease-out]"
      />

      <div
        ref={painel}
        role="dialog"
        aria-modal="true"
        aria-labelledby={idDoTitulo}
        aria-describedby={descricao ? idDaDescricao : undefined}
        className="relative flex h-full w-full max-w-md flex-col bg-superficie shadow-nivel-3 motion-safe:animate-[entrar-da-direita_200ms_ease-out]"
      >
        <header className="flex items-start justify-between gap-4 border-b border-borda px-5 py-4">
          <div className="min-w-0">
            {/* Recebe o foco ao abrir, mas não é controle: o anel não faz sentido nele. */}
            <h2
              ref={cabecalho}
              id={idDoTitulo}
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
    </div>
  );
}
