"use client";

import Link from "next/link";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import { Botao, Entrada, Selecao } from "@/componentes/controles";
import { competenciaAtual, formatarValor } from "@/lib/dinheiro";

const situacoes: Record<string, string> = {
  Ativo: "Ativo",
  Suspenso: "Suspenso",
  Encerrado: "Encerrado",
};

export default function ListagemDeContratos() {
  const clienteDeConsultas = useQueryClient();
  const hoje = competenciaAtual();

  const [ano, definirAno] = useState(hoje.ano);
  const [mes, definirMes] = useState(hoje.mes);

  const contratos = useQuery({
    queryKey: ["contratos"],
    queryFn: async () => {
      const { data, error } = await api.GET("/contratos");
      if (error || !data) throw new Error("Não foi possível carregar os contratos.");
      return data;
    },
  });

  const gerar = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/contratos/gerar-mensalidades", {
        body: { ano, mes, contratoIds: null },
      });
      if (error || !data) throw new Error("Não foi possível gerar as mensalidades.");
      return data;
    },
    onSuccess: () => clienteDeConsultas.invalidateQueries({ queryKey: ["recebiveis"] }),
  });

  const total = contratos.data
    ?.filter((contrato) => contrato.situacao === "Ativo")
    .reduce((soma, contrato) => soma + contrato.valor, 0);

  return (
    <>
      <header className="flex flex-wrap items-center justify-between gap-4 border-b border-borda bg-superficie px-6 py-4">
        <div>
          <h1 className="text-xl font-semibold tracking-tight text-marca-950">Contratos</h1>
          <p className="text-slate-500">O que cada cliente paga por mês, e desde quando.</p>
        </div>

        <Link
          href="/contratos/novo"
          className="inline-flex items-center rounded-[--radius-controle] bg-marca-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-marca-700"
        >
          Novo contrato
        </Link>
      </header>

      <div className="flex flex-1 flex-col gap-6 p-6">
        <section className="flex flex-col gap-3 rounded-[--radius-cartao] border border-borda bg-superficie p-5">
          <div>
            <h2 className="font-semibold text-marca-950">Gerar mensalidades</h2>
            <p className="text-slate-500">
              Cria um recebível para cada contrato ativo na competência escolhida. Pode ser
              executado quantas vezes for preciso: o que já existe é ignorado, nunca duplicado.
            </p>
          </div>

          <div className="flex flex-wrap items-end gap-3">
            <div className="w-28">
              <Selecao
                rotulo="Mês"
                value={mes}
                onChange={(evento) => definirMes(Number(evento.target.value))}
              >
                {Array.from({ length: 12 }, (_, indice) => indice + 1).map((numero) => (
                  <option key={numero} value={numero}>
                    {String(numero).padStart(2, "0")}
                  </option>
                ))}
              </Selecao>
            </div>

            <div className="w-28">
              <Entrada
                rotulo="Ano"
                inputMode="numeric"
                className="numeros-tabulares"
                value={ano}
                onChange={(evento) => definirAno(Number(evento.target.value) || hoje.ano)}
              />
            </div>

            <Botao type="button" disabled={gerar.isPending} onClick={() => gerar.mutate()}>
              {gerar.isPending ? "Gerando…" : "Gerar"}
            </Botao>
          </div>

          {gerar.data && (
            <p
              role="status"
              className="rounded-[--radius-controle] bg-marca-50 px-4 py-3 text-marca-800"
            >
              {gerar.data.recado}
              {gerar.data.ignoradas > 0 && ` ${gerar.data.ignoradas} já existia(m).`}
              {gerar.data.foraDeVigencia > 0 &&
                ` ${gerar.data.foraDeVigencia} contrato(s) fora de vigência.`}{" "}
              <Link href="/recebiveis" className="font-semibold underline underline-offset-2">
                Ver recebíveis
              </Link>
            </p>
          )}

          {gerar.isError && (
            <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
              {gerar.error.message}
            </p>
          )}
        </section>

        {contratos.isPending && <p className="text-slate-500">Carregando os contratos…</p>}

        {contratos.isError && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            {contratos.error.message}
          </p>
        )}

        {contratos.data && contratos.data.length === 0 && (
          <div className="rounded-[--radius-cartao] border border-dashed border-borda-forte bg-superficie px-6 py-12 text-center">
            <p className="font-medium text-slate-700">Nenhum contrato cadastrado.</p>
            <p className="mt-1 text-slate-500">
              Um contrato precisa de um cliente. Se ainda não há clientes, cadastre a pessoa e
              marque-a como cliente do escritório.
            </p>
          </div>
        )}

        {contratos.data && contratos.data.length > 0 && (
          <>
            <div className="overflow-x-auto rounded-[--radius-cartao] border border-borda bg-superficie shadow-nivel-1">
              <table className="w-full min-w-3xl border-collapse text-left">
                <thead>
                  <tr className="border-b border-borda text-xs tracking-wide text-slate-500 uppercase">
                    <th scope="col" className="px-4 py-3 font-semibold">Código</th>
                    <th scope="col" className="px-4 py-3 font-semibold">Cliente</th>
                    <th scope="col" className="px-4 py-3 font-semibold">Descrição</th>
                    <th scope="col" className="px-4 py-3 text-right font-semibold">Valor</th>
                    <th scope="col" className="px-4 py-3 text-right font-semibold">Vencimento</th>
                    <th scope="col" className="px-4 py-3 font-semibold">Situação</th>
                  </tr>
                </thead>
                <tbody>
                  {contratos.data.map((contrato) => (
                    <tr key={contrato.id} className="border-b border-borda last:border-0 hover:bg-marca-50">
                      <td className="numeros-tabulares px-4 py-3">
                        <Link
                          href={`/contratos/${contrato.id}`}
                          className="font-medium text-marca-700 hover:underline"
                        >
                          {contrato.codigo}
                        </Link>
                      </td>
                      <td className="px-4 py-3 text-slate-700">
                        <span className="numeros-tabulares text-slate-500">
                          {contrato.codigoDoCliente}
                        </span>{" "}
                        {contrato.nomeDoCliente}
                      </td>
                      <td className="px-4 py-3 text-slate-700">{contrato.descricao}</td>
                      <td className="numeros-tabulares px-4 py-3 text-right font-medium text-slate-800">
                        {formatarValor(contrato.valor)}
                      </td>
                      {/* Só o número: "dia" na frente de cada linha é ruído repetido. */}
                      <td className="numeros-tabulares px-4 py-3 text-right text-slate-700">
                        {contrato.diaDeVencimento}
                      </td>
                      <td className="px-4 py-3">
                        <span
                          className={
                            "rounded-full px-2 py-0.5 text-xs font-semibold " +
                            (contrato.situacao === "Ativo"
                              ? "bg-emerald-50 text-emerald-800"
                              : contrato.situacao === "Suspenso"
                                ? "bg-amber-50 text-amber-800"
                                : "bg-slate-100 text-slate-600")
                          }
                        >
                          {situacoes[contrato.situacao] ?? contrato.situacao}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <p className="text-slate-500">
              {contratos.data.length === 1 ? "1 contrato" : `${contratos.data.length} contratos`}
              {total !== undefined && (
                <>
                  {" · "}
                  <span className="numeros-tabulares font-medium text-slate-700">
                    {formatarValor(total)}
                  </span>{" "}
                  por mês em contratos ativos
                </>
              )}
            </p>
          </>
        )}
      </div>
    </>
  );
}
