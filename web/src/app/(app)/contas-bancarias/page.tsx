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
 * pergunta desta tela é quais existem e quanto cada uma tem. Tabela serve à
 * lista que se ordena e se recorta; esta cabe inteira na tela.
 * </p>
 * <p>
 * O saldo que aparece é o inicial mais os movimentos, somado na API. A tela
 * não soma nada: é a mesma regra de que total nenhum vem da página.
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
  const nenhumaRecebe = lista.length > 0 && !lista.some((conta) => conta.recebeCobrancas);

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

        {/* Sem conta marcada, a cobrança pelo PSP é recusada: melhor saber aqui do que na hora de cobrar. */}
        {nenhumaRecebe && (
          <p className="rounded-[--radius-controle] border border-amber-300 bg-amber-50 px-4 py-3 text-amber-900">
            Nenhuma conta está marcada para receber as cobranças do PSP, e por isso não dá para emitir
            cobrança. Altere a conta do PSP e marque essa opção.
          </p>
        )}

        {contas.data && lista.length === 0 && (
          <div className="rounded-[--radius-cartao] border border-dashed border-borda-forte bg-superficie px-6 py-12 text-center">
            <p className="font-medium text-slate-700">Nenhuma conta cadastrada.</p>
            <p className="mt-1 text-slate-600">
              Cadastre as contas que o escritório usa, com o saldo de um dia que dê para conferir no
              extrato. A baixa pede a conta onde o dinheiro entrou ou saiu.
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

                    <div className="flex shrink-0 flex-col items-end gap-1">
                      {!conta.ativa && (
                        <span className="rounded-full bg-slate-200 px-2 py-0.5 text-xs font-semibold text-slate-700">
                          Inativa
                        </span>
                      )}
                      {conta.recebeCobrancas && (
                        <span className="rounded-full bg-marca-50 px-2 py-0.5 text-xs font-semibold text-marca-800">
                          Recebe as cobranças
                        </span>
                      )}
                    </div>
                  </div>

                  <div>
                    <p className="text-xs font-semibold tracking-wide text-slate-500 uppercase">Saldo</p>
                    <p
                      className={
                        "numeros-tabulares text-2xl font-semibold " +
                        (conta.saldoAtual < 0 ? "text-red-700" : "text-slate-800")
                      }
                    >
                      {formatarValor(conta.saldoAtual)}
                    </p>
                    <p className="text-sm text-slate-500">
                      Começou com {formatarValor(conta.saldoInicial)} em {formatarData(conta.saldoInicialEm)}
                    </p>
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
