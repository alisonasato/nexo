"use client";

import Link from "next/link";
import { useState } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { useAvisos } from "@/componentes/avisos";
import { Botao, Entrada } from "@/componentes/controles";
import { formatarData, formatarValor } from "@/lib/dinheiro";
import { useConsultaDaUrl } from "@/lib/estado-na-url";

import { GavetaDeMovimento } from "./gaveta-de-movimento";
import { GavetaDeTransferencia } from "./gaveta-de-transferencia";

type MovimentoNoExtrato = components["schemas"]["MovimentoNoExtrato"];
type Problema = components["schemas"]["Problema"];

const compacto = "min-h-11 px-4 text-xs md:min-h-0 md:px-3 md:py-1";

/* Como cada origem se lê no extrato: é a primeira pergunta de quem concilia. */
const origens: Record<string, string> = {
  baixa: "Baixa",
  "baixa-estornada": "Baixa estornada pelo PSP",
  "estorno-psp": "Devolução pelo PSP",
  tarifa: "Tarifa",
  rendimento: "Rendimento",
  "entrada-avulsa": "Outra entrada",
  "saida-avulsa": "Outra saída",
  transferencia: "Transferência",
};

function descreverFalha(erro: unknown, padrao: string): string {
  const problemas =
    erro && typeof erro === "object" && "problemas" in erro && Array.isArray(erro.problemas)
      ? (erro.problemas as Problema[])
      : [];
  return problemas.length > 0 ? `${problemas[0].descricao} ${problemas[0].sugestao}` : padrao;
}

/**
 * O extrato de uma conta: o que entrou e saiu, com o saldo depois de cada linha.
 *
 * <p>
 * <b>É a tela de conciliar.</b> Quem abre tem o extrato do banco do lado, e
 * procura a linha que não bate. Por isso cada linha diz de onde veio, e o saldo
 * de cada uma está ali para comparar com o do banco no mesmo ponto.
 * </p>
 * <p>
 * <b>Apagar pede confirmação na própria linha.</b> Tarifa e transferência se
 * apagam daqui, e a transferência leva as duas pontas; um clique sem volta
 * mudaria o saldo de duas contas. Movimento de baixa não mostra o botão: ele
 * sai pelo estorno, na tela de lançamentos.
 * </p>
 */
export function ExtratoDaConta({ id }: { id: string }) {
  const clienteDeConsultas = useQueryClient();
  const avisar = useAvisos();
  const { ler, gravar, estabilizada } = useConsultaDaUrl(`/contas-bancarias/${id}`);

  /* Sem datas na URL, quem escolhe o período é a API: do começo do mês até hoje. */
  const de = ler("de") ?? "";
  const ate = ler("ate") ?? "";

  const [gaveta, definirGaveta] = useState<"movimento" | "transferencia" | null>(null);
  const [apagando, definirApagando] = useState<string | null>(null);

  const invalidar = () => {
    clienteDeConsultas.invalidateQueries({ queryKey: ["extrato"] });
    clienteDeConsultas.invalidateQueries({ queryKey: ["contas-bancarias"] });
    clienteDeConsultas.invalidateQueries({ queryKey: ["fluxo-de-caixa"] });
  };

  const extrato = useQuery({
    queryKey: ["extrato", id, de, ate],
    queryFn: async () => {
      const { data, error, response } = await api.GET("/contas-bancarias/{id}/extrato", {
        params: { path: { id }, query: { ...(de ? { de } : {}), ...(ate ? { ate } : {}) } },
      });

      if (response.status === 404) throw new Error("Esta conta não existe neste escritório.");
      if (error) throw new Error(descreverFalha(error, "Não foi possível carregar o extrato."));
      return data!;
    },
    enabled: estabilizada,
    placeholderData: keepPreviousData,
  });

  const apagar = useMutation({
    mutationFn: async (movimentoId: string) => {
      const { error } = await api.DELETE("/contas-bancarias/{id}/movimentos/{movimentoId}", {
        params: { path: { id, movimentoId } },
      });
      if (error) throw error;
    },
    onSuccess: () => {
      definirApagando(null);
      invalidar();
      avisar({ tom: "sucesso", titulo: "Movimento apagado." });
    },
    onError: (erro: unknown) =>
      avisar({
        tom: "erro",
        titulo: "O movimento não foi apagado.",
        detalhe: descreverFalha(erro, "Tente de novo em instantes."),
      }),
  });

  const resultado = extrato.data;

  function acao(movimento: MovimentoNoExtrato) {
    if (!movimento.podeApagar) {
      return (
        <span className="text-xs text-slate-400" title="Movimento de baixa: desfaça pelo estorno, em Lançamentos.">
          —
        </span>
      );
    }

    if (apagando === movimento.id) {
      return (
        <div className="flex flex-wrap items-center gap-2 md:justify-end">
          <span className="text-xs text-slate-700">
            {movimento.transferenciaId ? "Apagar as duas pontas?" : "Apagar?"}
          </span>
          <Botao aparencia="secundario" type="button" className={compacto} onClick={() => definirApagando(null)}>
            Voltar
          </Botao>
          <Botao
            aparencia="perigo"
            type="button"
            className={compacto}
            disabled={apagar.isPending}
            onClick={() => apagar.mutate(movimento.id)}
          >
            {apagar.isPending ? "Apagando…" : "Apagar"}
          </Botao>
        </div>
      );
    }

    return (
      <Botao aparencia="secundario" type="button" className={compacto} onClick={() => definirApagando(movimento.id)}>
        Apagar
      </Botao>
    );
  }

  return (
    <>
      <header className="flex flex-wrap items-center justify-between gap-4 border-b border-borda bg-superficie px-6 py-4">
        <div>
          <Link
            href="/contas-bancarias"
            className="text-sm font-semibold text-marca-700 underline-offset-2 hover:underline"
          >
            ← Contas bancárias
          </Link>
          <h1 id="conteudo" tabIndex={-1} className="text-xl font-semibold tracking-tight text-marca-950">
            {resultado?.nome ?? "Extrato"}
          </h1>
          <p className="text-slate-600">O que entrou e saiu desta conta, com o saldo depois de cada movimento.</p>
        </div>

        <div className="flex flex-wrap gap-2">
          <Botao aparencia="secundario" type="button" onClick={() => definirGaveta("transferencia")}>
            Transferir
          </Botao>
          <Botao type="button" onClick={() => definirGaveta("movimento")}>
            Lançar movimento
          </Botao>
        </div>
      </header>

      <div className="flex flex-1 flex-col gap-5 p-6">
        <section
          aria-label="Período"
          className="flex flex-wrap items-end gap-3 rounded-[--radius-cartao] border border-borda bg-superficie p-4 shadow-nivel-1"
        >
          <div className="w-full sm:w-44">
            <Entrada
              rotulo="De"
              type="date"
              value={de || resultado?.de || ""}
              onChange={(evento) => gravar({ de: evento.target.value })}
            />
          </div>
          <div className="w-full sm:w-44">
            <Entrada
              rotulo="Até"
              type="date"
              value={ate || resultado?.ate || ""}
              onChange={(evento) => gravar({ ate: evento.target.value })}
            />
          </div>
        </section>

        {extrato.isPending && <p className="text-slate-600">Carregando o extrato…</p>}

        {extrato.isError && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            {extrato.error.message}
          </p>
        )}

        {resultado && (
          <>
            <div className="grid gap-px overflow-hidden rounded-[--radius-cartao] border border-borda bg-borda sm:grid-cols-2">
              {[
                { rotulo: `Saldo no começo de ${formatarData(resultado.de)}`, valor: resultado.saldoNoInicio },
                { rotulo: `Saldo ao fim de ${formatarData(resultado.ate)}`, valor: resultado.saldoNoFim },
              ].map((cartao) => (
                <div key={cartao.rotulo} className="flex flex-col gap-1 bg-superficie px-5 py-4">
                  <span className="text-xs font-semibold tracking-wide text-slate-500 uppercase">{cartao.rotulo}</span>
                  <span
                    className={
                      "numeros-tabulares text-2xl font-semibold " +
                      (cartao.valor < 0 ? "text-red-700" : "text-slate-800")
                    }
                  >
                    {formatarValor(cartao.valor)}
                  </span>
                </div>
              ))}
            </div>

            {resultado.movimentos.length === 0 && (
              <div className="rounded-[--radius-cartao] border border-dashed border-borda-forte bg-superficie px-6 py-12 text-center">
                <p className="font-medium text-slate-700">Nenhum movimento neste período.</p>
                <p className="mt-1 text-slate-600">
                  Baixas de lançamentos entram aqui sozinhas. Tarifa, rendimento e transferência entram pelos
                  botões acima.
                </p>
              </div>
            )}

            {resultado.movimentos.length > 0 && (
              <>
                <ul className="flex flex-col gap-3 md:hidden">
                  {resultado.movimentos.map((movimento) => (
                    <li
                      key={movimento.id}
                      className="rounded-[--radius-cartao] border border-borda bg-superficie p-4 shadow-nivel-1"
                    >
                      <div className="flex items-baseline justify-between gap-3">
                        <span className="numeros-tabulares text-slate-600">{formatarData(movimento.data)}</span>
                        <span
                          className={
                            "numeros-tabulares font-semibold " +
                            (movimento.valor < 0 ? "text-red-700" : "text-emerald-700")
                          }
                        >
                          {formatarValor(movimento.valor)}
                        </span>
                      </div>
                      <p className="mt-1 font-medium text-slate-800">{movimento.descricao}</p>
                      <p className="text-sm text-slate-500">
                        {origens[movimento.origem] ?? movimento.origem} · saldo{" "}
                        <span className="numeros-tabulares">{formatarValor(movimento.saldoDepois)}</span>
                      </p>
                      <div className="mt-3 border-t border-borda pt-3">{acao(movimento)}</div>
                    </li>
                  ))}
                </ul>

                <div className="hidden overflow-x-auto rounded-[--radius-cartao] border border-borda bg-superficie shadow-nivel-1 md:block">
                  <table className="w-full min-w-3xl border-collapse text-left">
                    <caption className="sr-only">
                      Extrato de {resultado.nome}, de {formatarData(resultado.de)} a {formatarData(resultado.ate)}
                    </caption>
                    <thead>
                      <tr className="border-b border-borda bg-slate-50/60 text-xs tracking-wide text-slate-500 uppercase">
                        <th scope="col" className="px-4 py-3 font-semibold">
                          Data
                        </th>
                        <th scope="col" className="px-4 py-3 font-semibold">
                          Descrição
                        </th>
                        <th scope="col" className="px-4 py-3 text-right font-semibold">
                          Entrou
                        </th>
                        <th scope="col" className="px-4 py-3 text-right font-semibold">
                          Saiu
                        </th>
                        <th scope="col" className="px-4 py-3 text-right font-semibold">
                          Saldo
                        </th>
                        <th scope="col" className="px-4 py-3 text-right font-semibold">
                          <span className="sr-only">Ações</span>
                        </th>
                      </tr>
                    </thead>
                    <tbody>
                      {resultado.movimentos.map((movimento) => (
                        <tr key={movimento.id} className="border-b border-borda align-top last:border-0">
                          <td className="numeros-tabulares px-4 py-3 text-slate-700">{formatarData(movimento.data)}</td>
                          <td className="px-4 py-3">
                            <span className="text-slate-800">{movimento.descricao}</span>
                            <span className="block text-xs text-slate-500">
                              {origens[movimento.origem] ?? movimento.origem}
                            </span>
                          </td>
                          <td className="numeros-tabulares px-4 py-3 text-right text-emerald-700">
                            {movimento.valor > 0 ? formatarValor(movimento.valor) : ""}
                          </td>
                          <td className="numeros-tabulares px-4 py-3 text-right text-red-700">
                            {movimento.valor < 0 ? formatarValor(-movimento.valor) : ""}
                          </td>
                          <td
                            className={
                              "numeros-tabulares px-4 py-3 text-right font-medium " +
                              (movimento.saldoDepois < 0 ? "text-red-700" : "text-slate-800")
                            }
                          >
                            {formatarValor(movimento.saldoDepois)}
                          </td>
                          <td className="px-4 py-3 text-right">{acao(movimento)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </>
            )}
          </>
        )}
      </div>

      <GavetaDeMovimento
        key={gaveta === "movimento" ? "movimento-aberta" : "movimento-fechada"}
        contaId={id}
        aberta={gaveta === "movimento"}
        aoFechar={() => definirGaveta(null)}
        aoGravar={invalidar}
      />

      <GavetaDeTransferencia
        key={gaveta === "transferencia" ? "transferencia-aberta" : "transferencia-fechada"}
        origemId={id}
        aberta={gaveta === "transferencia"}
        aoFechar={() => definirGaveta(null)}
        aoGravar={invalidar}
      />
    </>
  );
}
