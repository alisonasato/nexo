"use client";

import Link from "next/link";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { Botao, Entrada, Selecao } from "@/componentes/controles";
import { competenciaAtual, formatarCompetencia, formatarData, formatarValor } from "@/lib/dinheiro";

import { GavetaDeRecorrencia, rotulosDeFrequencia, type EdicaoDeRecorrencia } from "./gaveta-de-recorrencia";

type RecorrenciaNaLista = components["schemas"]["RecorrenciaNaLista"];
type NaturezaLancamento = components["schemas"]["NaturezaLancamento"];

const compacto = "min-h-11 px-4 text-xs md:min-h-0 md:px-3 md:py-1";

const lados: [NaturezaLancamento, string][] = [
  ["Receber", "A receber"],
  ["Pagar", "A pagar"],
];

/**
 * O que se repete sem contrato por trás, e a geração do mês.
 *
 * <p>
 * <b>Gerar é um gesto, e não uma rotina.</b> Escolhe-se a competência e gera-se,
 * como nos contratos. Nada é criado sozinho, sem ninguém ver, e gerar de novo
 * não duplica: o que já existe é ignorado.
 * </p>
 * <p>
 * <b>A competência da geração não mora na URL</b>, pelo mesmo motivo dos
 * contratos: ela é o parâmetro de uma ação, e um link com mês e ano preenchidos
 * seria um convite a gerar a competência de outra pessoa.
 * </p>
 */
export default function ListagemDeRecorrencias() {
  const clienteDeConsultas = useQueryClient();
  const hoje = competenciaAtual();

  const [ano, definirAno] = useState(hoje.ano);
  const [mes, definirMes] = useState(hoje.mes);
  const [edicao, definirEdicao] = useState<EdicaoDeRecorrencia | null>(null);

  const recorrencias = useQuery({
    queryKey: ["recorrencias"],
    queryFn: async () => {
      const { data, error } = await api.GET("/recorrencias");
      if (error || !data) throw new Error("Não foi possível carregar as recorrências.");
      return data;
    },
  });

  const gerar = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/recorrencias/gerar", { body: { ano, mes } });
      if (error || !data) throw new Error("Não foi possível gerar os lançamentos.");
      return data;
    },
    onSuccess: () => clienteDeConsultas.invalidateQueries({ queryKey: ["lancamentos"] }),
  });

  const lista = recorrencias.data ?? [];
  const ativas = lista.filter((item) => item.ativa).length;

  function linha(item: RecorrenciaNaLista) {
    const vigencia = item.fimEm
      ? `de ${formatarData(item.inicioEm)} a ${formatarData(item.fimEm)}`
      : `desde ${formatarData(item.inicioEm)}`;

    return (
      <li
        key={item.id}
        className="flex flex-wrap items-start justify-between gap-3 border-b border-borda py-3 last:border-0"
      >
        <div className="min-w-0">
          <p className="font-medium text-slate-800">
            {item.descricao}
            {!item.ativa && (
              <span className="ml-2 rounded-full bg-slate-200 px-2 py-0.5 text-xs font-semibold text-slate-600">
                Inativa
              </span>
            )}
          </p>
          <p className="text-sm text-slate-600">
            {item.codigoDaPessoa} · {item.nomeDaPessoa}
          </p>
          <p className="text-sm text-slate-600">
            {rotulosDeFrequencia[item.frequencia]}, dia {item.diaDeVencimento} · {vigencia}
            {item.categoria ? ` · ${item.categoria}` : ""}
          </p>
        </div>

        <div className="flex items-center gap-3">
          <span className="numeros-tabulares font-medium text-slate-800">{formatarValor(item.valor)}</span>
          <Botao
            aparencia="secundario"
            type="button"
            className={compacto}
            onClick={() => definirEdicao({ tipo: "alterar", recorrencia: item })}
            aria-label={`Alterar ${item.descricao}`}
          >
            Alterar
          </Botao>
        </div>
      </li>
    );
  }

  return (
    <>
      <header className="flex flex-wrap items-center justify-between gap-4 border-b border-borda bg-superficie px-6 py-4">
        <div>
          <h1 id="conteudo" tabIndex={-1} className="text-xl font-semibold tracking-tight text-marca-950">
            Recorrências
          </h1>
          <p className="text-slate-600">
            O que se repete sem contrato por trás: o aluguel, a licença anual, o serviço que o cliente paga todo
            trimestre.
          </p>
        </div>
        <Botao type="button" onClick={() => definirEdicao({ tipo: "nova" })}>
          Nova recorrência
        </Botao>
      </header>

      <div className="flex flex-1 flex-col gap-6 p-6">
        <section
          aria-labelledby="titulo-da-geracao"
          className="flex flex-col gap-3 rounded-[--radius-cartao] border border-borda bg-superficie p-5"
        >
          <div>
            <h2 id="titulo-da-geracao" className="font-semibold text-marca-950">
              Gerar os lançamentos do mês
            </h2>
            <p className="text-slate-600">
              Cria um lançamento para cada recorrência ativa que cai na competência escolhida, a receber e a
              pagar. Pode ser executado quantas vezes for preciso: o que já existe é ignorado, nunca duplicado.
            </p>
          </div>

          <div className="flex flex-wrap items-end gap-3">
            <div className="w-28">
              <Selecao
                rotulo="Mês"
                value={mes}
                onChange={(evento) => {
                  definirMes(Number(evento.target.value));
                  gerar.reset();
                }}
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
                onChange={(evento) => {
                  definirAno(Number(evento.target.value) || hoje.ano);
                  gerar.reset();
                }}
              />
            </div>

            {/* O rótulo diz a competência: gerar o mês errado só se desfaz cancelando lançamento por lançamento. */}
            <Botao type="button" disabled={gerar.isPending || ativas === 0} onClick={() => gerar.mutate()}>
              {gerar.isPending ? "Gerando…" : `Gerar ${formatarCompetencia(ano, mes)}`}
            </Botao>
          </div>

          {gerar.data && (
            <p role="status" className="rounded-[--radius-controle] bg-marca-50 px-4 py-3 text-marca-800">
              {gerar.data.recado}
              {gerar.data.ignorados > 0 && ` ${gerar.data.ignorados} já existia(m).`}
              {gerar.data.foraDaVez > 0 &&
                ` ${gerar.data.foraDaVez} fora da vez: inativa, fora da vigência ou num mês que a frequência pula.`}{" "}
              <Link href="/lancamentos" className="font-semibold underline underline-offset-2">
                Ver lançamentos
              </Link>
            </p>
          )}

          {gerar.isError && (
            <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
              {gerar.error.message}
            </p>
          )}
        </section>

        <section aria-labelledby="titulo-das-recorrencias" className="flex flex-col gap-4">
          <h2 id="titulo-das-recorrencias" className="text-lg font-semibold text-marca-950">
            Cadastradas
          </h2>

          {recorrencias.isPending && <p className="text-slate-600">Carregando as recorrências…</p>}

          {recorrencias.isError && (
            <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
              {recorrencias.error.message}
            </p>
          )}

          {recorrencias.data && lista.length === 0 && (
            <div className="rounded-[--radius-cartao] border border-dashed border-borda-forte bg-superficie px-6 py-10 text-center">
              <p className="font-medium text-slate-700">Nenhuma recorrência ainda.</p>
              <p className="mx-auto mt-1 max-w-xl text-slate-600">
                Cadastre o que o escritório paga ou recebe sempre igual, como o aluguel ou a licença do sistema. A
                mensalidade de cliente continua nos contratos.
              </p>
              <Botao type="button" className="mt-4" onClick={() => definirEdicao({ tipo: "nova" })}>
                Nova recorrência
              </Botao>
            </div>
          )}

          {lista.length > 0 && (
            <div className="grid gap-4 lg:grid-cols-2">
              {lados.map(([natureza, rotulo]) => {
                const doLado = lista.filter((item) => item.natureza === natureza);

                return (
                  <div
                    key={natureza}
                    className="rounded-[--radius-cartao] border border-borda bg-superficie p-4 shadow-nivel-1"
                  >
                    <h3 className="mb-1 text-xs font-semibold tracking-wide text-slate-500 uppercase">{rotulo}</h3>
                    {doLado.length === 0 ? (
                      <p className="py-2 text-slate-500">Nenhuma recorrência {rotulo.toLowerCase()}.</p>
                    ) : (
                      <ul>{doLado.map(linha)}</ul>
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </section>
      </div>

      <GavetaDeRecorrencia
        key={edicao === null ? "fechada" : edicao.tipo === "nova" ? "nova" : edicao.recorrencia.id}
        edicao={edicao}
        aoFechar={() => definirEdicao(null)}
      />
    </>
  );
}
