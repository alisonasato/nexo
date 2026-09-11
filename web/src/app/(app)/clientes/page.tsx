"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import { Botao, Entrada, Selecao } from "@/componentes/controles";
import type { components } from "@/api/esquema";
import { formatarDocumento } from "@/lib/formato";

type Problema = components["schemas"]["Problema"];
type RegimeTributario = components["schemas"]["RegimeTributario"];

const regimes: Record<RegimeTributario, string> = {
  Mei: "MEI",
  SimplesNacional: "Simples Nacional",
  LucroPresumido: "Lucro Presumido",
  LucroReal: "Lucro Real",
  TerceiroSetor: "Terceiro Setor",
  PessoaFisica: "Pessoa Física",
};

function problemasDaResposta(erro: unknown): Problema[] {
  if (erro && typeof erro === "object" && "problemas" in erro && Array.isArray(erro.problemas)) {
    return erro.problemas as Problema[];
  }
  return [];
}

export default function ListagemDeClientes() {
  const clienteDeConsultas = useQueryClient();

  const [abrindo, definirAbrindo] = useState(false);
  const [pessoaId, definirPessoaId] = useState("");
  const [regime, definirRegime] = useState<RegimeTributario>("SimplesNacional");
  const [responsavel, definirResponsavel] = useState("");
  const [problemas, definirProblemas] = useState<Problema[]>([]);
  const [incluirInativos, definirIncluirInativos] = useState(false);

  /* Mesma assimetria da tela de pessoas: desligar confirma, religar não. */
  const [confirmando, definirConfirmando] = useState<string | null>(null);

  useEffect(() => {
    if (!confirmando) return;
    const relogio = setTimeout(() => definirConfirmando(null), 5000);
    return () => clearTimeout(relogio);
  }, [confirmando]);

  const clientes = useQuery({
    queryKey: ["clientes", incluirInativos],
    queryFn: async () => {
      const { data, error } = await api.GET("/clientes", {
        params: { query: incluirInativos ? { incluirInativos: true } : {} },
      });
      if (error || !data) throw new Error("Não foi possível carregar os clientes.");
      return data;
    },
  });

  const pessoas = useQuery({
    queryKey: ["pessoas", ""],
    enabled: abrindo,
    queryFn: async () => {
      const { data, error } = await api.GET("/pessoas", { params: { query: { tamanho: 200 } } });
      if (error || !data) throw new Error("Não foi possível carregar as pessoas.");
      return data;
    },
  });

  const vincular = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/clientes", {
        body: {
          pessoaId,
          regimeTributario: regime,
          responsavel,
          observacoes: "",
          ativo: true,
        },
      });
      if (error) throw error;
      return data;
    },
    onSuccess: () => {
      definirProblemas([]);
      definirAbrindo(false);
      definirPessoaId("");
      definirResponsavel("");
      clienteDeConsultas.invalidateQueries({ queryKey: ["clientes"] });
    },
    onError: (erro) => definirProblemas(problemasDaResposta(erro)),
  });

  /*
   * Desligar e religar têm endereço próprio na API, e não saem por um PUT com
   * `ativo` trocado: a listagem não traz `observacoes`, então o PUT daqui
   * mandaria o campo vazio e apagaria a anotação do vínculo.
   */
  const inativar = useMutation({
    mutationFn: async (id: string) => {
      const { error } = await api.DELETE("/clientes/{id}", { params: { path: { id } } });
      if (error) throw new Error("Não foi possível inativar este cliente.");
    },
    onSuccess: () => {
      definirConfirmando(null);
      clienteDeConsultas.invalidateQueries({ queryKey: ["clientes"] });
    },
  });

  const reativar = useMutation({
    mutationFn: async (id: string) => {
      const { error } = await api.POST("/clientes/{id}/reativar", { params: { path: { id } } });
      if (error) throw new Error("Não foi possível reativar este cliente.");
    },
    onSuccess: () => clienteDeConsultas.invalidateQueries({ queryKey: ["clientes"] }),
  });

  /*
   * Os vínculos que já existem, inativos inclusive.
   *
   * Não dá para reaproveitar a listagem da tela: ela obedece ao "Mostrar
   * inativos", e com a caixa desmarcada um cliente desligado sumiria daqui —
   * a pessoa voltaria a aparecer como disponível, e o vínculo novo seria
   * recusado com "já é cliente" só depois de preencher o formulário. O
   * vínculo é único independente de estar ligado ou não.
   */
  const vinculados = useQuery({
    queryKey: ["clientes", "todos-os-vinculos"],
    enabled: abrindo,
    queryFn: async () => {
      const { data, error } = await api.GET("/clientes", {
        params: { query: { incluirInativos: true } },
      });
      if (error || !data) throw new Error("Não foi possível conferir os vínculos existentes.");
      return data;
    },
  });

  const jaSaoClientes = new Set(vinculados.data?.map((cliente) => cliente.pessoaId));
  const disponiveis = pessoas.data?.itens.filter((pessoa) => !jaSaoClientes.has(pessoa.id)) ?? [];

  return (
    <>
      <header className="flex flex-wrap items-center justify-between gap-4 border-b border-borda bg-superficie px-6 py-4">
        <div>
          <h1 className="text-xl font-semibold tracking-tight text-marca-950">Clientes</h1>
          <p className="text-slate-500">
            Quem o escritório atende. Um cliente é uma pessoa já cadastrada com vínculo.
          </p>
        </div>

        <Botao type="button" onClick={() => definirAbrindo((atual) => !atual)}>
          {abrindo ? "Cancelar" : "Novo cliente"}
        </Botao>
      </header>

      <div className="flex flex-1 flex-col gap-4 p-6">
        {abrindo && (
          <form
            className="flex flex-col gap-4 rounded-[--radius-cartao] border border-borda bg-superficie p-5"
            onSubmit={(evento) => {
              evento.preventDefault();
              vincular.mutate();
            }}
          >
            <div>
              <h2 className="font-semibold text-marca-950">Tornar uma pessoa cliente</h2>
              <p className="text-slate-500">
                A pessoa precisa estar cadastrada. Não está na lista?{" "}
                <Link href="/pessoas/novo" className="font-semibold text-marca-700 hover:underline">
                  Cadastre primeiro
                </Link>
                .
              </p>
            </div>

            {problemas.length > 0 && (
              <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
                {problemas[0].descricao} {problemas[0].sugestao}
              </p>
            )}

            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              <div className="sm:col-span-2 lg:col-span-1">
                <Selecao
                  rotulo="Pessoa"
                  required
                  value={pessoaId}
                  onChange={(evento) => definirPessoaId(evento.target.value)}
                >
                  <option value="">
                    {pessoas.isPending || vinculados.isPending ? "Carregando…" : "Escolha uma pessoa…"}
                  </option>
                  {disponiveis.map((pessoa) => (
                    <option key={pessoa.id} value={pessoa.id}>
                      {pessoa.nomeFantasia || pessoa.nome} — {formatarDocumento(pessoa.documento)}
                    </option>
                  ))}
                </Selecao>
              </div>

              <Selecao
                rotulo="Regime tributário"
                value={regime}
                onChange={(evento) => definirRegime(evento.target.value as RegimeTributario)}
              >
                {Object.entries(regimes).map(([valor, rotulo]) => (
                  <option key={valor} value={valor}>
                    {rotulo}
                  </option>
                ))}
              </Selecao>

              <Entrada
                rotulo="Responsável"
                value={responsavel}
                onChange={(evento) => definirResponsavel(evento.target.value)}
                ajuda="Quem responde por este cliente no escritório."
              />
            </div>

            <div className="flex justify-end">
              <Botao type="submit" disabled={vincular.isPending || vinculados.isPending || !pessoaId}>
                {vincular.isPending ? "Vinculando…" : "Tornar cliente"}
              </Botao>
            </div>
          </form>
        )}

        <label className="flex w-fit items-center gap-2 text-slate-600">
          <input
            type="checkbox"
            checked={incluirInativos}
            onChange={(evento) => definirIncluirInativos(evento.target.checked)}
            className="size-4 rounded border-borda-forte accent-marca-600"
          />
          Mostrar inativos
        </label>

        {clientes.isPending && <p className="text-slate-500">Carregando os clientes…</p>}

        {clientes.data && clientes.data.length === 0 && !abrindo && (
          <div className="rounded-[--radius-cartao] border border-dashed border-borda-forte bg-superficie px-6 py-12 text-center">
            <p className="font-medium text-slate-700">Nenhum cliente ainda.</p>
            <p className="mt-1 text-slate-500">
              O contrato depende disto: sem cliente, não há o que cobrar.
            </p>
          </div>
        )}

        {clientes.data && clientes.data.length > 0 && (
          <div className="overflow-x-auto rounded-[--radius-cartao] border border-borda bg-superficie shadow-nivel-1">
            <table className="w-full min-w-3xl border-collapse text-left">
              <thead>
                <tr className="border-b border-borda text-xs tracking-wide text-slate-500 uppercase">
                  <th scope="col" className="px-4 py-3 font-semibold">Código</th>
                  <th scope="col" className="px-4 py-3 font-semibold">Nome</th>
                  <th scope="col" className="px-4 py-3 font-semibold">Documento</th>
                  <th scope="col" className="px-4 py-3 font-semibold">Regime</th>
                  <th scope="col" className="px-4 py-3 font-semibold">Responsável</th>
                  <th scope="col" className="px-4 py-3 font-semibold">
                    <span className="sr-only">Ações</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {clientes.data.map((cliente) => (
                  <tr key={cliente.id} className="border-b border-borda last:border-0 hover:bg-marca-50">
                    <td className="numeros-tabulares px-4 py-3 font-medium text-slate-800">
                      {cliente.codigo}
                    </td>
                    <td className="px-4 py-3 text-slate-700">
                      {cliente.nome}
                      {!cliente.ativo && (
                        <span className="ml-2 rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                          inativo
                        </span>
                      )}
                      {cliente.nomeFantasia && (
                        <span className="block text-slate-500">{cliente.nomeFantasia}</span>
                      )}
                    </td>
                    <td className="numeros-tabulares px-4 py-3 text-slate-700">
                      {formatarDocumento(cliente.documento)}
                    </td>
                    <td className="px-4 py-3 text-slate-700">
                      {regimes[cliente.regimeTributario] ?? cliente.regimeTributario}
                    </td>
                    <td className="px-4 py-3 text-slate-700">{cliente.responsavel || "—"}</td>
                    <td className="px-4 py-3 text-right">
                      {cliente.ativo ? (
                        <Botao
                          aparencia={confirmando === cliente.id ? "perigo" : "secundario"}
                          type="button"
                          disabled={inativar.isPending}
                          onClick={() =>
                            confirmando === cliente.id
                              ? inativar.mutate(cliente.id)
                              : definirConfirmando(cliente.id)
                          }
                          className="px-3 py-1 text-xs"
                        >
                          {confirmando === cliente.id ? "Confirmar?" : "Inativar"}
                        </Botao>
                      ) : (
                        <Botao
                          aparencia="secundario"
                          type="button"
                          disabled={reativar.isPending}
                          onClick={() => reativar.mutate(cliente.id)}
                          className="px-3 py-1 text-xs"
                        >
                          Reativar
                        </Botao>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </>
  );
}
