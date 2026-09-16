"use client";

import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { problemasDaResposta } from "@/lib/problemas";
import { useAvisos } from "@/componentes/avisos";
import { Botao, Entrada, EntradaMascarada } from "@/componentes/controles";
import { Gaveta } from "@/componentes/gaveta";
import {
  formatarCompetencia,
  formatarData,
  formatarValor,
  hojeIso,
  mascararDinheiro,
} from "@/lib/dinheiro";
import { dividirEmParcelas, limitarParcelas, MAXIMO_DE_PARCELAS, somarMeses } from "@/lib/parcelas";

type LancamentoNaLista = components["schemas"]["LancamentoNaLista"];
type Problema = components["schemas"]["Problema"];

const centavosDe = (digitos: string) => Number(digitos.replace(/\D/g, "") || "0");

/**
 * Nula quando fechada. Quem abre passa uma chave com o id do título, para cada
 * renegociação começar do zero em vez de herdar o que foi digitado na anterior.
 */
type Props = { titulo: LancamentoNaLista | null; aoFechar: () => void };

/**
 * Trocar um título em aberto por parcelas novas.
 *
 * <p>
 * <b>Renegociar não edita título.</b> O original vira renegociado e fica como
 * histórico do que era devido; as parcelas novas nascem apontando para ele. A
 * gaveta mostra o título antes de tudo, porque é o que se está desfazendo.
 * </p>
 * <p>
 * Juros, multa e desconto são valores em reais, digitados por quem negocia. A
 * taxa automática sobre o vencido ainda é decisão em aberto, e uma conta
 * escondida aqui decidiria por ela.
 * </p>
 */
export function GavetaDeRenegociacao({ titulo, aoFechar }: Props) {
  const avisar = useAvisos();
  const clienteDeConsultas = useQueryClient();

  const [parcelas, definirParcelas] = useState(1);
  /* Um mês adiante: renegociar quase sempre é pedir prazo. */
  const [primeiroVencimento, definirPrimeiroVencimento] = useState(() => somarMeses(hojeIso(), 1));
  const [juros, definirJuros] = useState("");
  const [multa, definirMulta] = useState("");
  const [desconto, definirDesconto] = useState("");
  const [motivo, definirMotivo] = useState("");
  const [problemas, definirProblemas] = useState<Problema[]>([]);

  const original = titulo ? Math.round(titulo.valor * 100) : 0;
  const total = original + centavosDe(juros) + centavosDe(multa) - centavosDe(desconto);
  const previa = total > 0 ? dividirEmParcelas(total, parcelas, primeiroVencimento) : [];

  const renegociar = useMutation({
    mutationFn: async () => {
      if (!titulo) throw new Error("Nenhum título escolhido.");

      const { data, error } = await api.POST("/lancamentos/{id}/renegociar", {
        params: { path: { id: titulo.id } },
        body: {
          parcelas,
          primeiroVencimento,
          juros: centavosDe(juros) / 100,
          multa: centavosDe(multa) / 100,
          desconto: centavosDe(desconto) / 100,
          motivo,
        },
      });
      if (error) throw error;
      return data!;
    },
    onSuccess: (acordo) => {
      clienteDeConsultas.invalidateQueries({ queryKey: ["lancamentos"] });
      avisar({
        tom: "sucesso",
        titulo:
          acordo.parcelas.length === 1
            ? "Título renegociado."
            : `Título renegociado em ${acordo.parcelas.length} parcelas.`,
      });
      aoFechar();
    },
    onError: (erro: unknown) => definirProblemas(problemasDaResposta(erro)),
  });

  const falhaSemMotivo = renegociar.isError && problemas.length === 0;
  const jaVenceu = titulo !== null && titulo.vencimento < hojeIso();

  return (
    <Gaveta
      titulo="Renegociar"
      descricao="O título atual vira renegociado e dá lugar a parcelas novas. Nada é apagado."
      aberta={titulo !== null}
      aoFechar={aoFechar}
      rodape={
        <div className="flex justify-end gap-2">
          <Botao aparencia="secundario" type="button" onClick={aoFechar}>
            Voltar
          </Botao>
          <Botao
            type="submit"
            form="formulario-de-renegociacao"
            disabled={renegociar.isPending || motivo.trim().length === 0 || previa.length === 0}
          >
            {renegociar.isPending ? "Renegociando…" : "Renegociar"}
          </Botao>
        </div>
      }
    >
      {titulo && (
        <form
          id="formulario-de-renegociacao"
          className="flex flex-col gap-4"
          onSubmit={(evento) => {
            evento.preventDefault();
            renegociar.mutate();
          }}
        >
          <div className="rounded-[--radius-controle] bg-slate-50 px-4 py-3">
            <p className="font-medium text-slate-800">{titulo.nomeDaPessoa}</p>
            <p className="text-slate-600">
              {titulo.descricao} · {formatarCompetencia(titulo.competenciaAno, titulo.competenciaMes)}
            </p>
            <p className="numeros-tabulares mt-1 text-slate-700">
              {formatarValor(titulo.valor)} · {jaVenceu ? "venceu em" : "vence em"}{" "}
              {formatarData(titulo.vencimento)}
            </p>
          </div>

          {(problemas.length > 0 || falhaSemMotivo) && (
            <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
              {problemas.length > 0
                ? `${problemas[0].descricao} ${problemas[0].sugestao}`
                : "Não foi possível renegociar. Tente de novo em instantes."}
            </p>
          )}

          <fieldset>
            <legend className="mb-2 text-xs font-semibold tracking-wide text-slate-600 uppercase">
              Acréscimos e desconto
            </legend>
            <div className="grid grid-cols-3 gap-3">
              <EntradaMascarada
                rotulo="Juros"
                className="text-right"
                digitos={juros}
                mascara={mascararDinheiro}
                aoMudar={definirJuros}
              />
              <EntradaMascarada
                rotulo="Multa"
                className="text-right"
                digitos={multa}
                mascara={mascararDinheiro}
                aoMudar={definirMulta}
              />
              <EntradaMascarada
                rotulo="Desconto"
                className="text-right"
                digitos={desconto}
                mascara={mascararDinheiro}
                aoMudar={definirDesconto}
              />
            </div>
          </fieldset>

          <div className="grid grid-cols-2 gap-3">
            <Entrada
              rotulo="Parcelas"
              type="number"
              inputMode="numeric"
              min={1}
              max={MAXIMO_DE_PARCELAS}
              value={parcelas}
              onChange={(evento) => definirParcelas(limitarParcelas(evento.target.value))}
            />
            <Entrada
              rotulo={parcelas > 1 ? "Primeiro vencimento" : "Vencimento"}
              type="date"
              required
              value={primeiroVencimento}
              onChange={(evento) => definirPrimeiroVencimento(evento.target.value)}
            />
          </div>

          <Entrada
            rotulo="Motivo"
            required
            value={motivo}
            onChange={(evento) => definirMotivo(evento.target.value)}
            ajuda="Quem olhar este acordo depois vai perguntar por quê."
          />

          <div className="rounded-[--radius-controle] border border-borda">
            <div className="flex items-baseline justify-between gap-3 border-b border-borda px-4 py-2">
              <span className="text-xs font-semibold tracking-wide text-slate-500 uppercase">Novo total</span>
              <span
                className={
                  "numeros-tabulares font-semibold " + (total > 0 ? "text-slate-800" : "text-red-700")
                }
              >
                {formatarValor(total / 100)}
              </span>
            </div>

            {total <= 0 ? (
              <p className="px-4 py-2 text-red-700">
                O desconto passa do título. Para perdoar a dívida inteira, cancele o título com o motivo.
              </p>
            ) : (
              <ol className="divide-y divide-borda">
                {previa.map((parcela) => (
                  <li key={parcela.numero} className="numeros-tabulares flex justify-between gap-3 px-4 py-2">
                    <span className="text-slate-600">
                      {previa.length > 1 ? `${parcela.numero} de ${previa.length} · ` : ""}vence{" "}
                      {formatarData(parcela.vencimento)}
                    </span>
                    <span className="font-medium text-slate-800">{formatarValor(parcela.centavos / 100)}</span>
                  </li>
                ))}
              </ol>
            )}
          </div>
        </form>
      )}
    </Gaveta>
  );
}
