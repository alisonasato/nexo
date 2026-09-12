"use client";

import { Botao } from "@/componentes/controles";

type Props = {
  quantidade: number;
  /** Habilitado só com uma marcada: editar é unitário. */
  aoEditar: () => void;
  aoInativar: () => void;
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
export function BarraDeSelecao({ quantidade, aoEditar, aoInativar, aoLimpar, ocupada }: Props) {
  if (quantidade === 0) return null;

  const uma = quantidade === 1;

  return (
    <div
      role="region"
      aria-label="Ações para a seleção"
      className="sticky bottom-4 z-30 mx-auto flex w-full max-w-2xl flex-wrap items-center gap-3 rounded-[--radius-cartao] border border-borda bg-marca-950 px-4 py-3 text-white shadow-nivel-2 motion-safe:animate-[subir_200ms_ease-out]"
    >
      <p aria-live="polite" className="flex-1 font-medium">
        {uma ? "1 cadastro selecionado" : `${quantidade} cadastros selecionados`}
      </p>

      <Botao
        aparencia="secundario"
        type="button"
        disabled={!uma || ocupada}
        onClick={aoEditar}
        /* Desabilitado quando há mais de uma: editar abre um cadastro, não vários. */
        title={uma ? undefined : "Selecione um único cadastro para editar"}
        className="px-3 py-1.5"
      >
        Editar
      </Botao>

      <Botao
        aparencia="perigo"
        type="button"
        disabled={ocupada}
        onClick={aoInativar}
        className="px-3 py-1.5"
      >
        {ocupada ? "Inativando…" : uma ? "Inativar" : `Inativar ${quantidade}`}
      </Botao>

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
