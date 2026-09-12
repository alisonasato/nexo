"use client";

import { createContext, useCallback, useContext, useEffect, useRef, useState } from "react";

type Tom = "sucesso" | "erro" | "informacao";

type Aviso = { id: number; tom: Tom; titulo: string; detalhe?: string };

type Emissor = (aviso: { tom: Tom; titulo: string; detalhe?: string }) => void;

const Contexto = createContext<Emissor | null>(null);

/**
 * Os avisos de canto, para dizer o que acabou de acontecer.
 *
 * <b>São para o desfecho de uma ação, não para narrar a tela.</b> Avisar a cada
 * clique de seleção transformaria o canto num piscar constante e ensinaria a
 * ignorar o canto — inclusive quando ele trouxesse o erro que importa. Quantos
 * itens estão marcados é estado contínuo, e estado contínuo se mostra na barra
 * de seleção, que fica parada na tela enquanto for verdade.
 */
export function ProvedorDeAvisos({ children }: { children: React.ReactNode }) {
  const [avisos, definirAvisos] = useState<Aviso[]>([]);
  const proximoId = useRef(1);

  const emitir = useCallback<Emissor>((aviso) => {
    const id = proximoId.current++;
    definirAvisos((atuais) => [...atuais, { ...aviso, id }]);
  }, []);

  const dispensar = useCallback((id: number) => {
    definirAvisos((atuais) => atuais.filter((aviso) => aviso.id !== id));
  }, []);

  return (
    <Contexto.Provider value={emitir}>
      {children}

      {/*
        `aria-live="polite"` e não `assertive`: o leitor de tela termina o que
        está falando antes de anunciar. E o canto não recebe foco — roubar o
        foco de quem está digitando para mostrar um aviso é pior que o aviso.
      */}
      <div
        aria-live="polite"
        aria-atomic="false"
        className="pointer-events-none fixed inset-x-4 bottom-4 z-50 flex flex-col items-end gap-2 sm:inset-x-auto sm:right-6 sm:bottom-6 sm:w-96"
      >
        {avisos.map((aviso) => (
          <CartaoDeAviso key={aviso.id} aviso={aviso} aoDispensar={() => dispensar(aviso.id)} />
        ))}
      </div>
    </Contexto.Provider>
  );
}

export function useAvisos(): Emissor {
  const emitir = useContext(Contexto);
  if (!emitir) throw new Error("useAvisos precisa estar dentro de ProvedorDeAvisos.");
  return emitir;
}

const tons: Record<Tom, { faixa: string; icone: React.ReactNode }> = {
  sucesso: {
    faixa: "bg-emerald-500",
    icone: <path d="m5 13 4 4L19 7" />,
  },
  erro: {
    faixa: "bg-red-500",
    icone: <path d="M12 8v5M12 17h.01M10.3 3.9 2.6 17a2 2 0 0 0 1.7 3h15.4a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z" />,
  },
  informacao: {
    faixa: "bg-marca-500",
    icone: <path d="M12 16v-5M12 8h.01M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18Z" />,
  },
};

function CartaoDeAviso({ aviso, aoDispensar }: { aviso: Aviso; aoDispensar: () => void }) {
  const [saindo, definirSaindo] = useState(false);

  /*
   * Erro fica mais tempo: ele costuma trazer o que fazer a seguir, e some antes
   * de a pessoa terminar de ler é a mesma coisa que não ter aparecido.
   */
  const duracao = aviso.tom === "erro" ? 7000 : 4000;

  useEffect(() => {
    const some = setTimeout(() => definirSaindo(true), duracao);
    const tira = setTimeout(aoDispensar, duracao + 200);
    return () => {
      clearTimeout(some);
      clearTimeout(tira);
    };
  }, [duracao, aoDispensar]);

  const { faixa, icone } = tons[aviso.tom];

  return (
    <div
      role={aviso.tom === "erro" ? "alert" : "status"}
      className={
        "pointer-events-auto flex w-full gap-3 overflow-hidden rounded-[--radius-cartao] border border-borda bg-superficie shadow-nivel-2 " +
        "motion-safe:transition-all motion-safe:duration-200 " +
        (saindo ? "opacity-0 motion-safe:translate-y-1" : "opacity-100")
      }
    >
      {/* A faixa colorida repete o que o ícone já diz: cor sozinha não informa. */}
      <span aria-hidden="true" className={`w-1 shrink-0 ${faixa}`} />

      <div className="flex flex-1 items-start gap-3 py-3 pr-2">
        <svg
          viewBox="0 0 24 24"
          aria-hidden="true"
          className={
            "mt-0.5 size-5 shrink-0 " +
            (aviso.tom === "sucesso"
              ? "text-emerald-600"
              : aviso.tom === "erro"
                ? "text-red-600"
                : "text-marca-600")
          }
          fill="none"
          stroke="currentColor"
          strokeWidth="1.8"
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          {icone}
        </svg>

        <div className="min-w-0 flex-1">
          <p className="font-medium text-slate-800">{aviso.titulo}</p>
          {aviso.detalhe && <p className="mt-0.5 text-slate-600">{aviso.detalhe}</p>}
        </div>

        <button
          type="button"
          onClick={aoDispensar}
          aria-label="Dispensar aviso"
          className="-mt-1 inline-flex size-8 shrink-0 items-center justify-center rounded-[--radius-controle] text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
        >
          <svg
            viewBox="0 0 24 24"
            aria-hidden="true"
            className="size-4"
            fill="none"
            stroke="currentColor"
            strokeWidth="1.8"
            strokeLinecap="round"
          >
            <path d="M6 6 18 18M18 6 6 18" />
          </svg>
        </button>
      </div>
    </div>
  );
}
