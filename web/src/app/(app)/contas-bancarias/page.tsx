"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { Botao } from "@/componentes/controles";
import { formatarData, formatarValor } from "@/lib/dinheiro";

import { GavetaDeConta, rotulosDeTipo } from "./gaveta-de-conta";

type ContaNaLista = components["schemas"]["ContaNaLista"];

function descreverConta(conta: ContaNaLista): string {
  return [
    rotulosDeTipo[conta.tipo],
    conta.banco,
    conta.agencia && `ag. ${conta.agencia}`,
    conta.numero && `conta ${conta.numero}`,
  ]
    .filter(Boolean)
    .join(" · ");
}

/**
 * As contas onde o dinheiro do escritório fica.
 *
 * <p>
 * <b>Cartões, e não tabela.</b> Um escritório tem três ou quatro contas, e a
 * pergunta desta tela é quais existem e com que saldo começaram. Tabela serve à
 * lista que se ordena e se recorta; esta cabe inteira na tela.
 * </p>
 * <p>
 * O saldo que aparece é o inicial, com o dia em que valia. O saldo de hoje
 * nasce quando as baixas passarem a dizer em que conta o dinheiro entrou ou
 * saiu.
 * </p>
 */
export default function ListagemDeContasBancarias() {
  const [editando, definirEditando] = useState<ContaNaLista | "nova" | null>(null);

  const contas = useQuery({
    queryKey: ["contas-bancarias"],
    queryFn: async () => {
      const { data, error } = await api.GET("/contas-bancarias");
      if (error || !data) throw new Error("Não foi possível carregar as contas.");
      return data;
    },
  });

  const lista = contas.data ?? [];

  return (
    <>
      <header className="flex flex-wrap items-center justify-between gap-4 border-b border-borda bg-superficie px-6 py-4">
        <div>
          <h1 id="conteudo" tabIndex={-1} className="text-xl font-semibold tracking-tight text-marca-950">
            Contas bancárias
          </h1>
          <p className="text-slate-600">Onde o dinheiro do escritório fica: bancos, a conta do PSP e o caixa.</p>
        </div>

        <Botao type="button" onClick={() => definirEditando("nova")}>
          Nova conta
        </Botao>
      </header>

      <div className="flex flex-1 flex-col gap-5 p-6">
        {contas.isPending && <p className="text-slate-600">Carregando as contas…</p>}

        {contas.isError && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            {contas.error.message}
          </p>
        )}

        {contas.data && lista.length === 0 && (
          <div className="rounded-[--radius-cartao] border border-dashed border-borda-forte bg-superficie px-6 py-12 text-center">
            <p className="font-medium text-slate-700">Nenhuma conta cadastrada.</p>
            <p className="mt-1 text-slate-600">
              Cadastre as contas que o escritório usa, com o saldo de um dia que dê para conferir no
              extrato.
            </p>
          </div>
        )}

        {lista.length > 0 && (
          <ul className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {lista.map((conta) => (
              <li key={conta.id}>
                <article
                  className={
                    "flex h-full flex-col gap-4 rounded-[--radius-cartao] border p-5 shadow-nivel-1 " +
                    (conta.ativa ? "border-borda bg-superficie" : "border-dashed border-borda-forte bg-slate-50")
                  }
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <h2 className="truncate font-semibold text-marca-950">{conta.nome}</h2>
                      <p className="text-sm text-slate-600">{descreverConta(conta)}</p>
                    </div>

                    {!conta.ativa && (
                      <span className="shrink-0 rounded-full bg-slate-200 px-2 py-0.5 text-xs font-semibold text-slate-700">
                        Inativa
                      </span>
                    )}
                  </div>

                  <div>
                    <p className="text-xs font-semibold tracking-wide text-slate-500 uppercase">Saldo inicial</p>
                    <p
                      className={
                        "numeros-tabulares text-2xl font-semibold " +
                        (conta.saldoInicial < 0 ? "text-red-700" : "text-slate-800")
                      }
                    >
                      {formatarValor(conta.saldoInicial)}
                    </p>
                    <p className="text-sm text-slate-500">no começo de {formatarData(conta.saldoInicialEm)}</p>
                  </div>

                  <div className="mt-auto">
                    <Botao
                      aparencia="secundario"
                      type="button"
                      className="min-h-11 px-4 text-xs md:min-h-0 md:px-3 md:py-1"
                      onClick={() => definirEditando(conta)}
                      aria-label={`Alterar ${conta.nome}`}
                    >
                      Alterar
                    </Botao>
                  </div>
                </article>
              </li>
            ))}
          </ul>
        )}
      </div>

      <GavetaDeConta
        key={editando === null ? "fechada" : editando === "nova" ? "nova" : editando.id}
        conta={editando}
        aoFechar={() => definirEditando(null)}
      />
    </>
  );
}
