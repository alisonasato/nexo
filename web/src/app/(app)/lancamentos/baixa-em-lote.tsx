"use client";

import Link from "next/link";
import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { useAvisos } from "@/componentes/avisos";
import { Botao, Entrada, Selecao } from "@/componentes/controles";
import { Gaveta } from "@/componentes/gaveta";
import { formatarData, formatarValor, hojeIso } from "@/lib/dinheiro";

import { useContasParaBaixa } from "./contas";

type LancamentoNaLista = components["schemas"]["LancamentoNaLista"];
type NaturezaLancamento = components["schemas"]["NaturezaLancamento"];

/** "1 baixado" e "3 baixados": o número manda no particípio. */
const concordar = (quantos: number, participio: string) =>
  `${quantos} ${participio}${quantos === 1 ? "" : "s"}`;

type Props = {
  natureza: NaturezaLancamento;
  selecionados: LancamentoNaLista[];
  aberta: boolean;
  aoFechar: () => void;
  aoConcluir: () => void;
};

/**
 * Baixar de uma vez o que foi marcado na lista.
 *
 * <p>
 * Cada lançamento é baixado pelo <b>valor dele</b>. Quem recebeu ou pagou valor
 * diferente em algum baixa aquele à mão, pela linha; o lote é para o caso comum,
 * o do extrato que bate.
 * </p>
 * <p>
 * <b>A soma que aparece aqui é do que foi marcado, e não um total da lista.</b>
 * A regra do projeto é que total nenhum vem da página, porque somar a página
 * dá um número errado com cara de certo. Esta soma não pretende responder
 * quanto o mês tem: ela confere, antes de confirmar, se a seleção bate com o
 * que o extrato mostra.
 * </p>
 */
export function BaixaEmLote({ natureza, selecionados, aberta, aoFechar, aoConcluir }: Props) {
  const avisar = useAvisos();
  const clienteDeConsultas = useQueryClient();
  const [pagoEm, definirPagoEm] = useState(hojeIso());
  const [contaDaBaixa, definirContaDaBaixa] = useState("");

  /* A primeira conta ativa, até alguém escolher outra. */
  const contas = useContasParaBaixa().data ?? [];
  const contaEscolhida = contas.some((conta) => conta.id === contaDaBaixa) ? contaDaBaixa : (contas[0]?.id ?? "");
  const aPagar = natureza === "Pagar";

  const baixar = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/lancamentos/baixar-em-lote", {
        body: { contaId: contaEscolhida, ids: selecionados.map((item) => item.id), pagoEm },
      });
      if (error || !data) throw new Error("Não foi possível baixar a seleção.");
      return data;
    },
    onSuccess: (resultado) => {
      clienteDeConsultas.invalidateQueries({ queryKey: ["lancamentos"] });
      clienteDeConsultas.invalidateQueries({ queryKey: ["divergencias"] });
      clienteDeConsultas.invalidateQueries({ queryKey: ["contas-bancarias"] });

      const recusados = resultado.recusados.length;

      if (recusados === 0) {
        avisar({
          tom: "sucesso",
          titulo:
            resultado.baixados === 1
              ? "1 lançamento baixado."
              : `${resultado.baixados} lançamentos baixados.`,
        });
      } else {
        avisar({
          tom: resultado.baixados === 0 ? "erro" : "informacao",
          titulo: `${concordar(resultado.baixados, "baixado")}, ${concordar(recusados, "recusado")}.`,
          /* O primeiro motivo por extenso: é o que diz o que fazer a seguir. */
          detalhe: resultado.recusados[0].motivo,
        });
      }

      aoConcluir();
    },
    onError: (erro) => avisar({ tom: "erro", titulo: erro.message }),
  });

  const soma = selecionados.reduce((total, item) => total + Math.round(item.valor * 100), 0) / 100;

  return (
    <Gaveta
      titulo="Baixar em lote"
      descricao="Cada lançamento é baixado pelo valor dele. Os que não puderem ser baixados voltam com o motivo."
      aberta={aberta}
      aoFechar={aoFechar}
      rodape={
        <div className="flex justify-end gap-2">
          <Botao aparencia="secundario" type="button" onClick={aoFechar}>
            Voltar
          </Botao>
          <Botao
            type="button"
            disabled={baixar.isPending || selecionados.length === 0 || !contaEscolhida}
            onClick={() => baixar.mutate()}
          >
            {baixar.isPending
              ? "Baixando…"
              : selecionados.length === 1
                ? "Baixar 1"
                : `Baixar ${selecionados.length}`}
          </Botao>
        </div>
      }
    >
      <div className="flex flex-col gap-4">
        <Entrada
          rotulo={aPagar ? "Data do pagamento" : "Data do recebimento"}
          type="date"
          required
          value={pagoEm}
          onChange={(evento) => definirPagoEm(evento.target.value)}
          ajuda={
            aPagar
              ? "Quando o dinheiro saiu, e não quando a baixa é registrada."
              : "Quando o dinheiro entrou, e não quando a baixa é registrada."
          }
        />

        <Selecao
          rotulo="Conta"
          value={contaEscolhida}
          onChange={(evento) => definirContaDaBaixa(evento.target.value)}
          ajuda={aPagar ? "De onde o dinheiro saiu, para todos os selecionados." : "Onde o dinheiro entrou, para todos os selecionados."}
        >
          {contas.map((conta) => (
            <option key={conta.id} value={conta.id}>
              {conta.nome}
            </option>
          ))}
        </Selecao>

        {contas.length === 0 && (
          <p role="alert" className="rounded-[--radius-controle] bg-amber-50 px-4 py-3 text-amber-900">
            Nenhuma conta ativa para receber a baixa.{" "}
            <Link href="/contas-bancarias" className="font-semibold underline underline-offset-2">
              Cadastre uma conta
            </Link>{" "}
            antes de baixar.
          </p>
        )}

        <div className="rounded-[--radius-controle] bg-slate-50 px-4 py-3">
          <p className="text-xs font-semibold tracking-wide text-slate-500 uppercase">
            Soma dos selecionados
          </p>
          <p className="numeros-tabulares text-2xl font-semibold text-slate-800">{formatarValor(soma)}</p>
        </div>

        <ul className="flex flex-col divide-y divide-borda">
          {selecionados.map((item) => (
            <li key={item.id} className="flex items-start justify-between gap-3 py-2">
              <span className="min-w-0">
                <span className="block truncate font-medium text-slate-800">{item.nomeDaPessoa}</span>
                <span className="block text-sm text-slate-500">
                  {item.descricao} · vence {formatarData(item.vencimento)}
                </span>
              </span>
              <span className="numeros-tabulares shrink-0 font-medium text-slate-800">
                {formatarValor(item.valor)}
              </span>
            </li>
          ))}
        </ul>
      </div>
    </Gaveta>
  );
}
