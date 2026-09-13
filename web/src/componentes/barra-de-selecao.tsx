"use client";

import type { ReactNode } from "react";

type Props = {
  quantidade: number;
  /** O que está sendo selecionado, para a contagem ler direito: "1 cadastro", "3 lançamentos". */
  substantivo: { singular: string; plural: string };
  /**
   * As ações desta tela sobre a seleção.
   *
   * A barra é a mesma em toda listagem; o que se faz com o que foi marcado
   * muda. Pessoas edita e inativa, lançamentos baixa em lote. Por isso as
   * ações entram como filhos, e não como propriedades com nome de uma tela só.
   */
  children: ReactNode;
  aoLimpar: () => void;
  ocupada?: boolean;
};

/**
 * A barra que aparece quando há linhas marcadas.
 *
 * <b>Barra, e não aviso de canto.</b> Quantas linhas estão marcadas é estado
 * contínuo: fica verdade enquanto ninguém desmarcar. Aviso é para desfecho, e
 * some sozinho — usar aviso aqui obrigaria a piscar o canto a cada clique e a
 * pessoa ficaria sem saber o total no momento de agir.
 *
 * Ela entra de baixo para cima e fica presa ao rodapé da tela: o polegar já
 * está lá no celular, e no desktop ela não empurra a tabela para baixo a cada
 * marcação.
 */
export function BarraDeSelecao({ quantidade, substantivo, children, aoLimpar, ocupada }: Props) {
  if (quantidade === 0) return null;

  return (
    <div
      role="region"
      aria-label="Ações para a seleção"
      className="sticky bottom-4 z-30 mx-auto flex w-full max-w-2xl flex-wrap items-center gap-3 rounded-[--radius-cartao] border border-borda bg-marca-950 px-4 py-3 text-white shadow-nivel-2 motion-safe:animate-[subir_200ms_ease-out]"
    >
      <p aria-live="polite" className="flex-1 font-medium">
        {quantidade === 1
          ? `1 ${substantivo.singular} selecionado`
          : `${quantidade} ${substantivo.plural} selecionados`}
      </p>

      {children}

      <button
        type="button"
        onClick={aoLimpar}
        disabled={ocupada}
        className="text-sm font-semibold text-marca-200 underline-offset-2 hover:underline disabled:text-marca-400"
      >
        Limpar
      </button>
    </div>
  );
}
