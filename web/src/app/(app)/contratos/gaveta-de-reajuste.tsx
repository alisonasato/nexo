"use client";

import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { problemasDaResposta } from "@/lib/problemas";
import { Botao, Entrada, Selecao } from "@/componentes/controles";
import { Gaveta } from "@/componentes/gaveta";
import { competenciaAtual, formatarCompetencia } from "@/lib/dinheiro";

type Problema = components["schemas"]["Problema"];

type Props = {
  /** Vazio quer dizer todos os contratos ativos, como na geração de mensalidades. */
  escolhidos: string[];
  aberta: boolean;
  aoFechar: () => void;
  aoReajustar: () => void;
};

/**
 * Reajustar contratos por percentual, a partir de uma competência.
 *
 * <p>
 * <b>O valor não é digitado: o percentual é.</b> Reajuste de carteira vem de um
 * índice, e digitar o valor novo de cada contrato é onde o erro mora. Cada
 * contrato ganha o próprio valor de agora corrigido pelo percentual.
 * </p>
 * <p>
 * <b>A competência é do reajuste, não de hoje.</b> Quem decide em dezembro que o
 * ano que vem sobe informa janeiro, e as mensalidades de dezembro continuam
 * saindo pelo valor velho.
 * </p>
 */
export function GavetaDeReajuste({ escolhidos, aberta, aoFechar, aoReajustar }: Props) {
  const clienteDeConsultas = useQueryClient();
  const proxima = competenciaAtual();

  const [percentual, definirPercentual] = useState("");
  const [mes, definirMes] = useState(proxima.mes);
  const [ano, definirAno] = useState(proxima.ano);
  const [motivo, definirMotivo] = useState("");
  const [problemas, definirProblemas] = useState<Problema[]>([]);

  const reajustar = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/contratos/reajustar", {
        body: {
          percentual: Number(percentual.replace(",", ".")),
          ano,
          mes,
          motivo,
          /* Nenhum escolhido quer dizer todos, e é `null` que diz isso à API. */
          contratoIds: escolhidos.length > 0 ? escolhidos : null,
        },
      });
      if (error) throw error;
      return data!;
    },
    onSuccess: () => {
      clienteDeConsultas.invalidateQueries({ queryKey: ["contratos"] });
      aoReajustar();
    },
    onError: (erro: unknown) => definirProblemas(problemasDaResposta(erro)),
  });

  const falhaSemMotivo = reajustar.isError && problemas.length === 0;
  const alcance =
    escolhidos.length > 0
      ? `${escolhidos.length} contrato${escolhidos.length > 1 ? "s" : ""} escolhido${escolhidos.length > 1 ? "s" : ""}`
      : "todos os contratos ativos";

  return (
    <Gaveta
      titulo="Reajustar contratos"
      descricao={`Atinge ${alcance}.`}
      aberta={aberta}
      aoFechar={aoFechar}
      rodape={
        <div className="flex justify-end gap-2">
          <Botao aparencia="secundario" type="button" onClick={aoFechar}>
            Cancelar
          </Botao>
          <Botao
            type="submit"
            form="formulario-de-reajuste"
            disabled={reajustar.isPending || percentual.trim().length === 0 || motivo.trim().length === 0}
          >
            {reajustar.isPending ? "Reajustando…" : `Reajustar a partir de ${formatarCompetencia(ano, mes)}`}
          </Botao>
        </div>
      }
    >
      <form
        id="formulario-de-reajuste"
        className="flex flex-col gap-4"
        onSubmit={(evento) => {
          evento.preventDefault();
          reajustar.mutate();
        }}
      >
        {(problemas.length > 0 || falhaSemMotivo) && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            {problemas.length > 0
              ? `${problemas[0].descricao} ${problemas[0].sugestao}`
              : "Não foi possível reajustar. Tente de novo em instantes."}
          </p>
        )}

        <Entrada
          rotulo="Percentual"
          inputMode="decimal"
          required
          className="numeros-tabulares text-right"
          value={percentual}
          onChange={(evento) => definirPercentual(evento.target.value)}
          ajuda="4,5 sobe 4,5%. Negativo reduz."
        />

        <div className="grid grid-cols-2 gap-4">
          <Selecao rotulo="A partir de" value={mes} onChange={(evento) => definirMes(Number(evento.target.value))}>
            {Array.from({ length: 12 }, (_, indice) => indice + 1).map((numero) => (
              <option key={numero} value={numero}>
                {String(numero).padStart(2, "0")}
              </option>
            ))}
          </Selecao>

          <Entrada
            rotulo="Ano"
            inputMode="numeric"
            className="numeros-tabulares"
            value={ano}
            onChange={(evento) => definirAno(Number(evento.target.value) || proxima.ano)}
          />
        </div>

        <Entrada
          rotulo="Motivo"
          required
          value={motivo}
          onChange={(evento) => definirMotivo(evento.target.value)}
          ajuda="De onde veio o índice: “IPCA de 2026”, “reajuste anual combinado”. Fica no histórico."
        />

        <p className="text-sm text-slate-600">
          Cada contrato ganha o próprio valor de hoje corrigido pelo percentual, numa vigência nova. As
          mensalidades de antes da competência escolhida continuam com o valor velho, e reajustar de novo a mesma
          competência não compõe o percentual.
        </p>
      </form>
    </Gaveta>
  );
}
