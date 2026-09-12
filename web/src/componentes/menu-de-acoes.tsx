"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";

import { IconeDeMaisAcoes } from "@/componentes/icones";

export type AcaoDeLinha =
  | { tipo: "link"; rotulo: string; href: string; icone: React.ReactNode }
  | {
      tipo: "botao";
      rotulo: string;
      icone: React.ReactNode;
      /** Devolve o texto de confirmação, quando houver algo a confirmar. */
      executar: () => string | void | Promise<string | void>;
    };

/**
 * As ações secundárias de uma linha, atrás de três pontos.
 *
 * <b>Fora da linha porque linha de tabela não é barra de ferramentas.</b> Um
 * botão solto por ação multiplica pelo número de linhas: vinte cadastros com
 * três ações viram sessenta alvos disputando a atenção de quem só queria ler o
 * telefone de alguém. Atrás dos três pontos, o custo é um clique a mais para
 * quem vai agir, e zero ruído para quem não vai.
 *
 * A ação mais comum — abrir o cadastro — <b>não</b> está aqui dentro: ela é o
 * link da primeira coluna, alcançável sem menu nenhum.
 */
export function MenuDeAcoes({ rotulo, acoes }: { rotulo: string; acoes: AcaoDeLinha[] }) {
  const [aberto, definirAberto] = useState(false);
  const [confirmacao, definirConfirmacao] = useState<string | null>(null);
  const area = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!aberto) return;

    function aoTeclar(evento: KeyboardEvent) {
      if (evento.key === "Escape") definirAberto(false);
    }

    function aoClicar(evento: MouseEvent) {
      if (!area.current?.contains(evento.target as Node)) definirAberto(false);
    }

    document.addEventListener("keydown", aoTeclar);
    document.addEventListener("mousedown", aoClicar, true);

    return () => {
      document.removeEventListener("keydown", aoTeclar);
      document.removeEventListener("mousedown", aoClicar, true);
    };
  }, [aberto]);

  /* A confirmação some sozinha: é recado, não estado. */
  useEffect(() => {
    if (!confirmacao) return;
    const relogio = setTimeout(() => definirConfirmacao(null), 2500);
    return () => clearTimeout(relogio);
  }, [confirmacao]);

  return (
    <div ref={area} className="relative flex justify-end">
      <button
        type="button"
        onClick={() => definirAberto((estava) => !estava)}
        aria-expanded={aberto}
        aria-haspopup="menu"
        aria-label={`Mais ações para ${rotulo}`}
        /*
          A área de toque é maior que o desenho. Três pontos têm uns 4px de
          largura visível, e mirar nisso num celular é loteria.
        */
        className="inline-flex size-11 items-center justify-center rounded-[--radius-controle] text-slate-600 transition-colors hover:bg-slate-100 hover:text-slate-800 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-marca-500"
      >
        <IconeDeMaisAcoes className="size-5" />
      </button>

      {confirmacao && (
        <span
          role="status"
          className="absolute top-full right-0 z-10 mt-1 rounded-[--radius-controle] bg-slate-800 px-2 py-1 text-xs whitespace-nowrap text-white"
        >
          {confirmacao}
        </span>
      )}

      {aberto && (
        <div
          role="menu"
          aria-label={`Ações para ${rotulo}`}
          className="absolute top-full right-0 z-20 mt-1 w-56 overflow-hidden rounded-[--radius-cartao] border border-borda bg-superficie py-1 shadow-nivel-2"
        >
          {acoes.map((acao) =>
            acao.tipo === "link" ? (
              <Link
                key={acao.rotulo}
                role="menuitem"
                href={acao.href}
                onClick={() => definirAberto(false)}
                className="flex items-center gap-2 px-3 py-2 text-slate-700 transition-colors hover:bg-slate-50"
              >
                <span className="text-slate-400">{acao.icone}</span>
                {acao.rotulo}
              </Link>
            ) : (
              <button
                key={acao.rotulo}
                type="button"
                role="menuitem"
                onClick={async () => {
                  definirAberto(false);
                  const recado = await acao.executar();
                  if (recado) definirConfirmacao(recado);
                }}
                className="flex w-full items-center gap-2 px-3 py-2 text-left text-slate-700 transition-colors hover:bg-slate-50"
              >
                <span className="text-slate-400">{acao.icone}</span>
                {acao.rotulo}
              </button>
            ),
          )}
        </div>
      )}
    </div>
  );
}
