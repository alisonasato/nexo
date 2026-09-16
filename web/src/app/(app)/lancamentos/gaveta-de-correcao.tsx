"use client";

import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { useAvisos } from "@/componentes/avisos";
import { Botao, Entrada, EntradaMascarada, Selecao } from "@/componentes/controles";
import { Gaveta } from "@/componentes/gaveta";
import { digitosDoValor, formatarCompetencia, mascararDinheiro, valorDosDigitos } from "@/lib/dinheiro";
import { problemasDaResposta } from "@/lib/problemas";

type LancamentoNaLista = components["schemas"]["LancamentoNaLista"];
type Problema = components["schemas"]["Problema"];

/** Nulo quando fechada. A chave com o id faz cada abertura começar do que está gravado. */
type Props = { lancamento: LancamentoNaLista | null; aoFechar: () => void };

/**
 * Corrigir um lançamento em aberto.
 *
 * <p>
 * <b>Corrigir, e não relançar.</b> Antes disto, consertar um valor digitado
 * errado era cancelar e lançar de novo: duas linhas no histórico onde bastava
 * uma. A mudança fica na trilha, e aparece no Histórico da própria linha.
 * </p>
 * <p>
 * A pessoa não muda: lançamento de outro cliente é outro lançamento.
 * </p>
 */
export function GavetaDeCorrecao({ lancamento, aoFechar }: Props) {
  const avisar = useAvisos();
  const clienteDeConsultas = useQueryClient();

  const [descricao, definirDescricao] = useState(lancamento?.descricao ?? "");
  const [digitos, definirDigitos] = useState(digitosDoValor(lancamento?.valor));
  const [vencimento, definirVencimento] = useState(lancamento?.vencimento ?? "");
  const [mes, definirMes] = useState(lancamento?.competenciaMes ?? 1);
  const [ano, definirAno] = useState(lancamento?.competenciaAno ?? 2026);
  const [problemas, definirProblemas] = useState<Problema[]>([]);

  const corrigir = useMutation({
    mutationFn: async () => {
      const { error } = await api.PUT("/lancamentos/{id}", {
        params: { path: { id: lancamento!.id } },
        body: {
          descricao,
          valor: valorDosDigitos(digitos),
          vencimento,
          competenciaAno: ano,
          competenciaMes: mes,
        },
      });
      if (error) throw error;
    },
    onSuccess: () => {
      clienteDeConsultas.invalidateQueries({ queryKey: ["lancamentos"] });
      avisar({ tom: "sucesso", titulo: "Lançamento corrigido." });
      aoFechar();
    },
    onError: (erro: unknown) => definirProblemas(problemasDaResposta(erro)),
  });

  const falhaSemMotivo = corrigir.isError && problemas.length === 0;

  return (
    <Gaveta
      titulo="Corrigir lançamento"
      descricao={lancamento ? `${lancamento.nomeDaPessoa} · ${formatarCompetencia(ano, mes)}` : undefined}
      aberta={lancamento !== null}
      aoFechar={aoFechar}
      rodape={
        <div className="flex justify-end gap-2">
          <Botao aparencia="secundario" type="button" onClick={aoFechar}>
            Cancelar
          </Botao>
          <Botao
            type="submit"
            form="formulario-de-correcao"
            disabled={corrigir.isPending || descricao.trim().length === 0 || digitos.length === 0}
          >
            {corrigir.isPending ? "Salvando…" : "Salvar"}
          </Botao>
        </div>
      }
    >
      <form
        id="formulario-de-correcao"
        className="flex flex-col gap-4"
        onSubmit={(evento) => {
          evento.preventDefault();
          corrigir.mutate();
        }}
      >
        {(problemas.length > 0 || falhaSemMotivo) && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            {problemas.length > 0
              ? `${problemas[0].descricao} ${problemas[0].sugestao}`
              : "Não foi possível corrigir. Tente de novo em instantes."}
          </p>
        )}

        <Entrada
          rotulo="Descrição"
          required
          value={descricao}
          onChange={(evento) => definirDescricao(evento.target.value)}
        />

        <div className="grid grid-cols-2 gap-4">
          <EntradaMascarada
            rotulo="Valor"
            required
            className="text-right"
            digitos={digitos}
            mascara={mascararDinheiro}
            aoMudar={definirDigitos}
          />
          <Entrada
            rotulo="Vencimento"
            type="date"
            required
            value={vencimento}
            onChange={(evento) => definirVencimento(evento.target.value)}
          />
        </div>

        <div className="grid grid-cols-2 gap-4">
          <Selecao rotulo="Competência" value={mes} onChange={(evento) => definirMes(Number(evento.target.value))}>
            {Array.from({ length: 12 }, (_, indice) => indice + 1).map((numero) => (
              <option key={numero} value={numero}>
                {formatarCompetencia(ano, numero).split("/")[0]}
              </option>
            ))}
          </Selecao>
          <Entrada
            rotulo="Ano"
            inputMode="numeric"
            className="numeros-tabulares"
            value={ano}
            onChange={(evento) => definirAno(Number(evento.target.value) || ano)}
            ajuda="O mês do serviço, não o do vencimento."
          />
        </div>

        <p className="text-sm text-slate-600">
          Vale enquanto está em aberto e sem cobrança emitida. A correção fica no Histórico.
        </p>
      </form>
    </Gaveta>
  );
}
