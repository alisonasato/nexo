"use client";

import Link from "next/link";

import { Botao } from "@/componentes/controles";

type Props = {
  titulo: string;
  descricao: string;
  /** Quando existe, aparece o botão de tentar de novo. */
  tentarDeNovo?: () => void;
  /** O identificador que o Next gera em produção. É o que se cita ao pedir ajuda. */
  digest?: string;
};

/**
 * O que a pessoa vê quando algo quebra.
 *
 * <b>Três coisas, e nenhuma é a pilha de chamadas.</b> O que aconteceu em uma
 * frase, um jeito de sair do lugar, e o identificador do erro — que é a única
 * coisa útil de se copiar quando for pedir ajuda.
 *
 * A mensagem técnica fica de fora de propósito. Quem opera um escritório de
 * contabilidade não tem o que fazer com "TypeError: cannot read properties of
 * undefined", e mostrar isso só transfere o susto sem transferir a solução. O
 * detalhe está no console do navegador e no registro do servidor, onde quem
 * conserta vai procurar.
 */
export function TelaDeErro({ titulo, descricao, tentarDeNovo, digest }: Props) {
  return (
    <div className="flex flex-1 items-center justify-center p-6">
      <div className="w-full max-w-md rounded-[--radius-cartao] border border-borda bg-superficie p-8 text-center shadow-nivel-1">
        <h1 className="text-xl font-semibold tracking-tight text-marca-950">{titulo}</h1>
        <p className="mt-2 text-slate-500">{descricao}</p>

        <div className="mt-6 flex flex-wrap items-center justify-center gap-3">
          {tentarDeNovo && (
            <Botao type="button" onClick={tentarDeNovo}>
              Tentar de novo
            </Botao>
          )}

          <Link
            href="/pessoas"
            className="inline-flex items-center rounded-[--radius-controle] border border-borda-forte bg-superficie px-4 py-2 text-sm font-semibold text-slate-700 transition-colors hover:bg-slate-50"
          >
            Ir para Pessoas
          </Link>
        </div>

        {digest && (
          <p className="mt-6 border-t border-borda pt-4 text-xs text-slate-400">
            Código do erro: <span className="numeros-tabulares">{digest}</span>
          </p>
        )}
      </div>
    </div>
  );
}
