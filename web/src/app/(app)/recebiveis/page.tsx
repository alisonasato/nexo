"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import { Botao, Entrada } from "@/componentes/controles";
import type { components } from "@/api/esquema";
import { formatarCompetencia, formatarData, formatarValor, lerValor } from "@/lib/dinheiro";

type SituacaoRecebivel = components["schemas"]["SituacaoRecebivel"];

const hojeIso = () => new Date().toISOString().slice(0, 10);

export default function ListagemDeRecebiveis() {
  const clienteDeConsultas = useQueryClient();

  const [situacao, definirSituacao] = useState<SituacaoRecebivel | "">("");

  /* Qual recebível está com o formulário de baixa aberto. */
  const [baixando, definirBaixando] = useState<string | null>(null);
  const [valorDigitado, definirValorDigitado] = useState("");
  const [dataDaBaixa, definirDataDaBaixa] = useState(hojeIso());
  const [falha, definirFalha] = useState<string | null>(null);

  const recebiveis = useQuery({
    queryKey: ["recebiveis", situacao],
    queryFn: async () => {
      const { data, error } = await api.GET("/recebiveis", {
        params: { query: situacao ? { situacao } : {} },
      });
      if (error || !data) throw new Error("Não foi possível carregar os recebíveis.");
      return data;
    },
  });

  const baixar = useMutation({
    mutationFn: async (id: string) => {
      const { data, error } = await api.POST("/recebiveis/{id}/baixar", {
        params: { path: { id } },
        body: { valorPago: lerValor(valorDigitado), pagoEm: dataDaBaixa },
      });
      if (error) throw error;
      return data;
    },
    onSuccess: () => {
      definirBaixando(null);
      definirFalha(null);
      clienteDeConsultas.invalidateQueries({ queryKey: ["recebiveis"] });
    },
    onError: (erro: unknown) => {
      const problemas =
        erro && typeof erro === "object" && "problemas" in erro && Array.isArray(erro.problemas)
          ? (erro.problemas as components["schemas"]["Problema"][])
          : [];
      definirFalha(
        problemas.length > 0
          ? `${problemas[0].descricao} ${problemas[0].sugestao}`
          : "Não foi possível registrar a baixa.",
      );
    },
  });

  const estornar = useMutation({
    mutationFn: async (id: string) => {
      const { error } = await api.POST("/recebiveis/{id}/estornar", {
        params: { path: { id } },
      });
      if (error) throw new Error("Não foi possível estornar.");
    },
    onSuccess: () => clienteDeConsultas.invalidateQueries({ queryKey: ["recebiveis"] }),
  });

  const resumo = recebiveis.data;
  const hoje = hojeIso();

  return (
    <>
      <header className="border-b border-borda bg-superficie px-6 py-4">
        <h1 className="text-xl font-semibold tracking-tight text-marca-950">Recebíveis</h1>
        <p className="text-slate-500">O que o escritório tem a receber, e o que já entrou.</p>
      </header>

      <div className="flex flex-1 flex-col gap-5 p-6">
        {resumo && (
          <div className="grid gap-px overflow-hidden rounded-[--radius-cartao] border border-borda bg-borda sm:grid-cols-3">
            {[
              { rotulo: "Em aberto", valor: resumo.totalEmAberto, tom: "text-slate-800" },
              { rotulo: "Vencido", valor: resumo.totalVencido, tom: "text-red-700" },
              { rotulo: "Recebido", valor: resumo.totalRecebido, tom: "text-emerald-700" },
            ].map((tile) => (
              <div key={tile.rotulo} className="flex flex-col gap-1 bg-superficie px-5 py-4">
                <span className="text-xs font-semibold tracking-wide text-slate-500 uppercase">
                  {tile.rotulo}
                </span>
                <span className={`numeros-tabulares text-2xl font-semibold ${tile.tom}`}>
                  {formatarValor(tile.valor)}
                </span>
              </div>
            ))}
          </div>
        )}

        <div className="flex flex-wrap items-center gap-2">
          {([
            ["", "Todos"],
            ["Aberto", "Em aberto"],
            ["Pago", "Pagos"],
          ] as const).map(([valor, rotulo]) => (
            <button
              key={rotulo}
              type="button"
              onClick={() => definirSituacao(valor as SituacaoRecebivel | "")}
              aria-pressed={situacao === valor}
              className={
                "rounded-full px-3 py-1 text-sm font-medium transition-colors " +
                (situacao === valor
                  ? "bg-marca-600 text-white"
                  : "border border-borda-forte bg-superficie text-slate-700 hover:bg-slate-50")
              }
            >
              {rotulo}
            </button>
          ))}
        </div>

        {recebiveis.isPending && <p className="text-slate-500">Carregando os recebíveis…</p>}

        {recebiveis.isError && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            {recebiveis.error.message}
          </p>
        )}

        {resumo && resumo.itens.length === 0 && (
          <div className="rounded-[--radius-cartao] border border-dashed border-borda-forte bg-superficie px-6 py-12 text-center">
            <p className="font-medium text-slate-700">Nenhum recebível nesta seleção.</p>
            <p className="mt-1 text-slate-500">
              As mensalidades saem dos contratos, em Contratos → Gerar mensalidades.
            </p>
          </div>
        )}

        {resumo && resumo.itens.length > 0 && (
          <div className="overflow-x-auto rounded-[--radius-cartao] border border-borda bg-superficie shadow-nivel-1">
            <table className="w-full min-w-3xl border-collapse text-left">
              <thead>
                <tr className="border-b border-borda text-xs tracking-wide text-slate-500 uppercase">
                  <th scope="col" className="px-4 py-3 font-semibold">Cliente</th>
                  <th scope="col" className="px-4 py-3 font-semibold">Competência</th>
                  <th scope="col" className="px-4 py-3 font-semibold">Vencimento</th>
                  <th scope="col" className="px-4 py-3 text-right font-semibold">Valor</th>
                  <th scope="col" className="px-4 py-3 font-semibold">Situação</th>
                  <th scope="col" className="px-4 py-3 text-right font-semibold">
                    <span className="sr-only">Ações</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {resumo.itens.map((item) => {
                  const vencido = item.situacao === "Aberto" && item.vencimento < hoje;
                  const emBaixa = baixando === item.id;

                  return (
                    <tr key={item.id} className="border-b border-borda last:border-0 align-top">
                      <td className="px-4 py-3">
                        <span className="numeros-tabulares text-slate-500">
                          {item.codigoDoCliente}
                        </span>{" "}
                        <span className="text-slate-800">{item.nomeDoCliente}</span>
                        <span className="block text-slate-500">{item.descricao}</span>
                      </td>
                      <td className="px-4 py-3 text-slate-700">
                        {formatarCompetencia(item.competenciaAno, item.competenciaMes)}
                      </td>
                      <td
                        className={
                          "numeros-tabulares px-4 py-3 " +
                          (vencido ? "font-semibold text-red-700" : "text-slate-700")
                        }
                      >
                        {formatarData(item.vencimento)}
                      </td>
                      <td className="numeros-tabulares px-4 py-3 text-right font-medium text-slate-800">
                        {formatarValor(item.valor)}
                        {/* != null cobre nulo e ausente: o contrato admite os dois. */}
                        {item.valorPago != null && item.valorPago !== item.valor && (
                          <span className="block text-emerald-700">
                            recebido {formatarValor(item.valorPago)}
                          </span>
                        )}
                      </td>
                      <td className="px-4 py-3">
                        <span
                          className={
                            "rounded-full px-2 py-0.5 text-xs font-semibold " +
                            (item.situacao === "Pago"
                              ? "bg-emerald-50 text-emerald-800"
                              : vencido
                                ? "bg-red-50 text-red-800"
                                : "bg-slate-100 text-slate-600")
                          }
                        >
                          {item.situacao === "Pago" ? "Pago" : vencido ? "Vencido" : "Em aberto"}
                        </span>
                        {item.pagoEm && (
                          <span className="mt-1 block text-xs text-slate-500">
                            {formatarData(item.pagoEm)}
                          </span>
                        )}
                      </td>
                      <td className="px-4 py-3 text-right">
                        {item.situacao === "Pago" ? (
                          <Botao
                            aparencia="secundario"
                            type="button"
                            className="px-3 py-1 text-xs"
                            disabled={estornar.isPending}
                            onClick={() => estornar.mutate(item.id)}
                          >
                            Estornar
                          </Botao>
                        ) : emBaixa ? (
                          <div className="flex flex-col items-end gap-2">
                            <div className="flex flex-wrap items-end justify-end gap-2">
                              <div className="w-32">
                                <Entrada
                                  rotulo="Recebido"
                                  inputMode="decimal"
                                  className="numeros-tabulares text-right"
                                  autoFocus
                                  value={valorDigitado}
                                  onChange={(evento) => definirValorDigitado(evento.target.value)}
                                />
                              </div>
                              <div className="w-40">
                                <Entrada
                                  rotulo="Data"
                                  type="date"
                                  value={dataDaBaixa}
                                  onChange={(evento) => definirDataDaBaixa(evento.target.value)}
                                />
                              </div>
                            </div>

                            <div className="flex gap-2">
                              <Botao
                                aparencia="secundario"
                                type="button"
                                className="px-3 py-1 text-xs"
                                onClick={() => {
                                  definirBaixando(null);
                                  definirFalha(null);
                                }}
                              >
                                Cancelar
                              </Botao>
                              <Botao
                                type="button"
                                className="px-3 py-1 text-xs"
                                disabled={baixar.isPending}
                                onClick={() => baixar.mutate(item.id)}
                              >
                                {baixar.isPending ? "Registrando…" : "Confirmar"}
                              </Botao>
                            </div>

                            {falha && (
                              <p role="alert" className="max-w-xs text-right text-xs text-red-700">
                                {falha}
                              </p>
                            )}
                          </div>
                        ) : (
                          <Botao
                            aparencia="secundario"
                            type="button"
                            className="px-3 py-1 text-xs"
                            onClick={() => {
                              definirBaixando(item.id);
                              definirFalha(null);
                              /* O valor cobrado já vem preenchido: é o caso comum. */
                              definirValorDigitado(
                                item.valor.toLocaleString("pt-BR", {
                                  minimumFractionDigits: 2,
                                  maximumFractionDigits: 2,
                                }),
                              );
                              definirDataDaBaixa(hojeIso());
                            }}
                          >
                            Baixar
                          </Botao>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}

        {resumo && resumo.itens.length > 0 && (
          <p className="text-slate-500">
            {resumo.quantidade === 1 ? "1 recebível" : `${resumo.quantidade} recebíveis`}
          </p>
        )}
      </div>
    </>
  );
}
