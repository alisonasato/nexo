"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import { Botao, Entrada, EntradaMascarada, Selecao } from "@/componentes/controles";
import { Paginacao } from "@/componentes/paginacao";
import type { components } from "@/api/esquema";
import {
  competenciaAtual,
  digitosDoValor,
  formatarCompetencia,
  formatarData,
  formatarValor,
  mascararDinheiro,
  valorDosDigitos,
} from "@/lib/dinheiro";

type SituacaoRecebivel = components["schemas"]["SituacaoRecebivel"];
type Problema = components["schemas"]["Problema"];

const hojeIso = () => new Date().toISOString().slice(0, 10);

function problemasDaResposta(erro: unknown): Problema[] {
  if (erro && typeof erro === "object" && "problemas" in erro && Array.isArray(erro.problemas)) {
    return erro.problemas as Problema[];
  }
  return [];
}

export default function ListagemDeRecebiveis() {
  const clienteDeConsultas = useQueryClient();

  const [situacao, definirSituacao] = useState<SituacaoRecebivel | "">("");
  const [pagina, definirPagina] = useState(1);

  /* Qual recebível está com o formulário de baixa aberto. */
  const [baixando, definirBaixando] = useState<string | null>(null);
  const [valorDigitado, definirValorDigitado] = useState("");
  const [dataDaBaixa, definirDataDaBaixa] = useState(hojeIso());
  const [falha, definirFalha] = useState<string | null>(null);

  /* Qual recebível está com o formulário de cancelamento aberto, e por quê. */
  const [cancelando, definirCancelando] = useState<string | null>(null);
  const [motivo, definirMotivo] = useState("");

  /* A cobrança avulsa: o que o escritório faz e não é mensalidade. */
  const competencia = competenciaAtual();
  const [lancando, definirLancando] = useState(false);
  const [clienteDoAvulso, definirClienteDoAvulso] = useState("");
  const [descricaoDoAvulso, definirDescricaoDoAvulso] = useState("");
  const [valorDoAvulso, definirValorDoAvulso] = useState("");
  const [vencimentoDoAvulso, definirVencimentoDoAvulso] = useState(hojeIso());
  const [anoDoAvulso, definirAnoDoAvulso] = useState(competencia.ano);
  const [mesDoAvulso, definirMesDoAvulso] = useState(competencia.mes);
  const [problemasDoAvulso, definirProblemasDoAvulso] = useState<Problema[]>([]);

  const recebiveis = useQuery({
    queryKey: ["recebiveis", situacao, pagina],
    queryFn: async () => {
      const { data, error } = await api.GET("/recebiveis", {
        params: { query: { pagina, ...(situacao ? { situacao } : {}) } },
      });
      if (error || !data) throw new Error("Não foi possível carregar os recebíveis.");
      return data;
    },
  });

  const baixar = useMutation({
    mutationFn: async (id: string) => {
      const { data, error } = await api.POST("/recebiveis/{id}/baixar", {
        params: { path: { id } },
        body: { valorPago: valorDosDigitos(valorDigitado), pagoEm: dataDaBaixa },
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

  const cancelar = useMutation({
    mutationFn: async (id: string) => {
      const { error } = await api.POST("/recebiveis/{id}/cancelar", {
        params: { path: { id } },
        body: { motivo },
      });
      if (error) throw error;
    },
    onSuccess: () => {
      definirCancelando(null);
      definirMotivo("");
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
          : "Não foi possível cancelar.",
      );
    },
  });

  /* Só quem carrega o papel de cliente: cobrar um fornecedor por engano de
     escolha na lista é erro que só aparece na hora de receber. */
  const clientes = useQuery({
    queryKey: ["pessoas", "clientes"],
    enabled: lancando,
    queryFn: async () => {
      const { data, error } = await api.GET("/pessoas", {
        params: { query: { papel: "Cliente", tamanho: 200 } },
      });
      if (error || !data) throw new Error("Não foi possível carregar os clientes.");
      return data.itens;
    },
  });

  const lancarAvulso = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/recebiveis", {
        body: {
          pessoaId: clienteDoAvulso,
          descricao: descricaoDoAvulso,
          valor: valorDosDigitos(valorDoAvulso),
          vencimento: vencimentoDoAvulso,
          competenciaAno: anoDoAvulso,
          competenciaMes: mesDoAvulso,
        },
      });
      if (error) throw error;
      return data;
    },
    onSuccess: () => {
      definirProblemasDoAvulso([]);
      definirLancando(false);
      definirClienteDoAvulso("");
      definirDescricaoDoAvulso("");
      definirValorDoAvulso("");
      clienteDeConsultas.invalidateQueries({ queryKey: ["recebiveis"] });
    },
    onError: (erro) => definirProblemasDoAvulso(problemasDaResposta(erro)),
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
      <header className="flex flex-wrap items-center justify-between gap-4 border-b border-borda bg-superficie px-6 py-4">
        <div>
          <h1 className="text-xl font-semibold tracking-tight text-marca-950">Recebíveis</h1>
          <p className="text-slate-500">O que o escritório tem a receber, e o que já entrou.</p>
        </div>

        <Botao type="button" onClick={() => definirLancando((atual) => !atual)}>
          {lancando ? "Cancelar" : "Nova cobrança"}
        </Botao>
      </header>

      <div className="flex flex-1 flex-col gap-5 p-6">
        {lancando && (
          <form
            className="flex flex-col gap-4 rounded-[--radius-cartao] border border-borda bg-superficie p-5"
            onSubmit={(evento) => {
              evento.preventDefault();
              lancarAvulso.mutate();
            }}
          >
            <div>
              <h2 className="font-semibold text-marca-950">Cobrança avulsa</h2>
              <p className="text-slate-500">
                Para o que não é mensalidade: declaração de imposto de renda, abertura de empresa,
                certidão. Não fica preso a contrato nenhum.
              </p>
            </div>

            {problemasDoAvulso.length > 0 && (
              <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
                {problemasDoAvulso[0].descricao} {problemasDoAvulso[0].sugestao}
              </p>
            )}

            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <div className="sm:col-span-2">
                <Selecao
                  rotulo="Cliente"
                  required
                  value={clienteDoAvulso}
                  onChange={(evento) => definirClienteDoAvulso(evento.target.value)}
                >
                  <option value="">
                    {clientes.isPending ? "Carregando…" : "Escolha um cliente…"}
                  </option>
                  {clientes.data?.map((cliente) => (
                    <option key={cliente.id} value={cliente.id}>
                      {cliente.codigo} — {cliente.nomeFantasia || cliente.nome}
                    </option>
                  ))}
                </Selecao>
              </div>

              <div className="sm:col-span-2">
                <Entrada
                  rotulo="Descrição"
                  required
                  value={descricaoDoAvulso}
                  onChange={(evento) => definirDescricaoDoAvulso(evento.target.value)}
                  ajuda="O que está sendo cobrado."
                />
              </div>

              <EntradaMascarada
                rotulo="Valor"
                required
                className="text-right"
                digitos={valorDoAvulso}
                mascara={mascararDinheiro}
                aoMudar={definirValorDoAvulso}
                ajuda="Os centavos entram primeiro."
              />

              <Entrada
                rotulo="Vencimento"
                type="date"
                required
                value={vencimentoDoAvulso}
                onChange={(evento) => definirVencimentoDoAvulso(evento.target.value)}
              />

              <Selecao
                rotulo="Competência"
                value={mesDoAvulso}
                onChange={(evento) => definirMesDoAvulso(Number(evento.target.value))}
              >
                {Array.from({ length: 12 }, (_, indice) => (
                  <option key={indice + 1} value={indice + 1}>
                    {formatarCompetencia(anoDoAvulso, indice + 1).split("/")[0]}
                  </option>
                ))}
              </Selecao>

              <Entrada
                rotulo="Ano da competência"
                inputMode="numeric"
                value={anoDoAvulso}
                onChange={(evento) => definirAnoDoAvulso(Number(evento.target.value))}
                /* A competência é o mês do serviço, e o vencimento é quando o
                   dinheiro entra. Confundir os dois erra o fechamento do mês. */
                ajuda="O mês do serviço, não o do vencimento."
              />
            </div>

            <div className="flex justify-end">
              <Botao
                type="submit"
                disabled={lancarAvulso.isPending || !clienteDoAvulso || !descricaoDoAvulso}
              >
                {lancarAvulso.isPending ? "Lançando…" : "Lançar cobrança"}
              </Botao>
            </div>
          </form>
        )}

        {resumo && (
          <div className="grid gap-px overflow-hidden rounded-[--radius-cartao] border border-borda bg-borda sm:grid-cols-3">
            {/*
              Estes números descrevem o período inteiro, não a página nem o
              filtro de situação — é a pergunta que o escritório faz enquanto
              olha uma fatia: quanto do mês ainda falta entrar.
            */}
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
            ["Cancelado", "Cancelados"],
          ] as const).map(([valor, rotulo]) => (
            <button
              key={rotulo}
              type="button"
              onClick={() => {
                definirSituacao(valor as SituacaoRecebivel | "");
                definirPagina(1);
              }}
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
                  const emCancelamento = cancelando === item.id;

                  return (
                    <tr key={item.id} className="border-b border-borda last:border-0 align-top">
                      <td className="px-4 py-3">
                        <span className="numeros-tabulares text-slate-500">
                          {item.codigoDaPessoa}
                        </span>{" "}
                        <span className="text-slate-800">{item.nomeDaPessoa}</span>
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
                              : item.situacao === "Cancelado"
                                ? "bg-slate-100 text-slate-500 line-through"
                                : vencido
                                  ? "bg-red-50 text-red-800"
                                  : "bg-slate-100 text-slate-600")
                          }
                        >
                          {item.situacao === "Pago"
                            ? "Pago"
                            : item.situacao === "Cancelado"
                              ? "Cancelado"
                              : vencido
                                ? "Vencido"
                                : "Em aberto"}
                        </span>
                        {item.motivoDoCancelamento && (
                          <span className="mt-1 block max-w-48 text-xs text-slate-500">
                            {item.motivoDoCancelamento}
                          </span>
                        )}
                        {item.pagoEm && (
                          <span className="mt-1 block text-xs text-slate-500">
                            {formatarData(item.pagoEm)}
                          </span>
                        )}
                      </td>
                      <td className="px-4 py-3 text-right">
                        {item.situacao === "Cancelado" ? (
                          <span className="text-xs text-slate-400">—</span>
                        ) : emCancelamento ? (
                          <div className="flex flex-col items-end gap-2">
                            <div className="w-56">
                              <Entrada
                                rotulo="Motivo do cancelamento"
                                autoFocus
                                value={motivo}
                                onChange={(evento) => definirMotivo(evento.target.value)}
                                ajuda="Quem olhar isto daqui a seis meses vai perguntar."
                              />
                            </div>

                            <div className="flex gap-2">
                              <Botao
                                aparencia="secundario"
                                type="button"
                                className="px-3 py-1 text-xs"
                                onClick={() => {
                                  definirCancelando(null);
                                  definirMotivo("");
                                  definirFalha(null);
                                }}
                              >
                                Voltar
                              </Botao>
                              <Botao
                                aparencia="perigo"
                                type="button"
                                className="px-3 py-1 text-xs"
                                disabled={cancelar.isPending || motivo.trim().length === 0}
                                onClick={() => cancelar.mutate(item.id)}
                              >
                                {cancelar.isPending ? "Cancelando…" : "Confirmar"}
                              </Botao>
                            </div>

                            {falha && (
                              <p role="alert" className="max-w-xs text-right text-xs text-red-700">
                                {falha}
                              </p>
                            )}
                          </div>
                        ) : item.situacao === "Pago" ? (
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
                                <EntradaMascarada
                                  rotulo="Recebido"
                                  className="text-right"
                                  autoFocus
                                  digitos={valorDigitado}
                                  mascara={mascararDinheiro}
                                  aoMudar={definirValorDigitado}
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
                          <div className="flex justify-end gap-2">
                            <Botao
                              aparencia="secundario"
                              type="button"
                              className="px-3 py-1 text-xs"
                              onClick={() => {
                                definirCancelando(item.id);
                                definirMotivo("");
                                definirFalha(null);
                              }}
                            >
                              Cancelar
                            </Botao>
                            <Botao
                              aparencia="secundario"
                              type="button"
                              className="px-3 py-1 text-xs"
                              onClick={() => {
                                definirBaixando(item.id);
                                definirFalha(null);
                                /* O valor cobrado já vem preenchido: é o caso comum. */
                                definirValorDigitado(digitosDoValor(item.valor));
                                definirDataDaBaixa(hojeIso());
                              }}
                            >
                              Baixar
                            </Botao>
                          </div>
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
          <Paginacao
            pagina={resumo.pagina}
            tamanho={resumo.tamanho}
            total={resumo.total}
            aoMudar={definirPagina}
            substantivo={{ singular: "recebível", plural: "recebíveis" }}
          />
        )}
      </div>
    </>
  );
}
