"use client";

import Link from "next/link";
import { useEffect, useState, useSyncExternalStore } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import { Paginacao } from "@/componentes/paginacao";
import { GerenciadorDeColunas } from "@/componentes/gerenciador-de-colunas";
import type { components } from "@/api/esquema";
import {
  assinarEscolhas,
  colunasVisiveis,
  escolhasEmUso,
  escolhasNoServidor,
  gravarEscolhas,
} from "./colunas";

type Papel = components["schemas"]["Papel"];

const papeis: Papel[] = ["Cliente", "Fornecedor", "Vendedor", "Colaborador"];

/* Traçado, e não emoji: o ícone acompanha o peso do texto ao lado. */
function EngrenagemIcone() {
  return (
    <svg
      viewBox="0 0 24 24"
      aria-hidden="true"
      className="size-4"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <circle cx="12" cy="12" r="3" />
      <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09a1.65 1.65 0 0 0-1.08-1.51 1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" />
    </svg>
  );
}

export default function ListagemDePessoas() {
  const [busca, definirBusca] = useState("");
  const [termo, definirTermo] = useState("");
  const [pagina, definirPagina] = useState(1);
  const [incluirInativas, definirIncluirInativas] = useState(false);
  const [papel, definirPapel] = useState<Papel | "">("");
  const [configurando, definirConfigurando] = useState(false);

  const escolhas = useSyncExternalStore(assinarEscolhas, escolhasEmUso, escolhasNoServidor);
  const colunas = colunasVisiveis(escolhas);

  /*
   * A listagem não inativa ninguém, de propósito. Inativar exige olhar o
   * cadastro — quantos contratos a pessoa tem, o que ainda deve — e essa
   * conferência não cabe numa linha de tabela. Sair daqui só para descobrir
   * que não podia era o caminho mais provável, e a ação mudou para dentro do
   * cadastro, onde a informação para decidir já está na tela.
   */

  /*
   * O termo só vira consulta depois de meio segundo parado. Sem isso, cada
   * tecla digitada é uma ida ao servidor — e num cadastro grande a busca fica
   * pior justamente para quem tem mais dados.
   */
  useEffect(() => {
    const relogio = setTimeout(() => {
      definirTermo(busca);
      /* Busca nova recomeça na primeira página: a sétima do termo velho não existe mais. */
      definirPagina(1);
    }, 500);
    return () => clearTimeout(relogio);
  }, [busca]);

  const pessoas = useQuery({
    queryKey: ["pessoas", termo, papel, pagina, incluirInativas],
    queryFn: async () => {
      const { data, error } = await api.GET("/pessoas", {
        params: {
          query: {
            pagina,
            ...(termo ? { busca: termo } : {}),
            ...(papel ? { papel } : {}),
            ...(incluirInativas ? { incluirInativos: true } : {}),
          },
        },
      });
      if (error || !data) throw new Error("Não foi possível carregar o cadastro.");
      return data;
    },
    /* A lista antiga fica na tela enquanto a nova chega: sem piscar a cada letra. */
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <header className="flex flex-wrap items-center justify-between gap-4 border-b border-borda bg-superficie px-6 py-4">
        <div>
          <h1 className="text-xl font-semibold tracking-tight text-marca-950">Pessoas</h1>
          <p className="text-slate-500">Clientes, fornecedores e demais cadastros do escritório.</p>
        </div>

        <div className="relative flex items-center gap-2">
          <button
            type="button"
            onClick={() => definirConfigurando((aberto) => !aberto)}
            aria-expanded={configurando}
            aria-haspopup="dialog"
            className="inline-flex items-center gap-2 rounded-[--radius-controle] border border-borda-forte bg-superficie px-3 py-2 text-sm font-semibold text-slate-700 transition-colors hover:bg-slate-50"
          >
            <EngrenagemIcone />
            Colunas
          </button>

          {configurando && (
            <GerenciadorDeColunas
              escolhas={escolhas}
              aoFechar={() => definirConfigurando(false)}
              aoAplicar={(novas) => {
                gravarEscolhas(novas);
                definirConfigurando(false);
              }}
            />
          )}

          <Link
            href="/pessoas/novo"
            className="inline-flex items-center rounded-[--radius-controle] bg-marca-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-marca-700"
          >
            Nova pessoa
          </Link>
        </div>
      </header>

      <div className="flex flex-1 flex-col gap-4 p-6">
        <div className="flex items-center gap-3">
          <input
            type="search"
            value={busca}
            onChange={(evento) => definirBusca(evento.target.value)}
            placeholder="Buscar por nome, nome fantasia ou documento"
            aria-label="Buscar pessoas"
            className="w-full max-w-md rounded-[--radius-controle] border border-borda-forte bg-superficie px-3 py-2 placeholder:text-slate-400 focus:border-marca-500"
          />
          <label className="flex shrink-0 items-center gap-2 text-slate-600">
            <input
              type="checkbox"
              checked={incluirInativas}
              onChange={(evento) => {
                definirIncluirInativas(evento.target.checked);
                /* A página sete sem os inativos não é a página sete com eles. */
                definirPagina(1);
              }}
              className="size-4 rounded border-borda-forte accent-marca-600"
            />
            Mostrar inativas
          </label>

          {pessoas.isFetching && <span className="text-xs text-slate-500">buscando…</span>}
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {(["", ...papeis] as (Papel | "")[]).map((valor) => (
            <button
              key={valor || "todos"}
              type="button"
              onClick={() => {
                definirPapel(valor);
                definirPagina(1);
              }}
              aria-pressed={papel === valor}
              className={
                "rounded-full px-3 py-1 text-sm font-medium transition-colors " +
                (papel === valor
                  ? "bg-marca-600 text-white"
                  : "border border-borda-forte bg-superficie text-slate-700 hover:bg-slate-50")
              }
            >
              {valor || "Todos"}
            </button>
          ))}
        </div>

        {pessoas.isError && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            {pessoas.error.message} Verifique se a API está no ar e tente de novo.
          </p>
        )}

        {pessoas.isPending && <p className="text-slate-500">Carregando o cadastro…</p>}

        {pessoas.data && pessoas.data.itens.length === 0 && (
          <div className="rounded-[--radius-cartao] border border-dashed border-borda-forte bg-superficie px-6 py-12 text-center">
            <p className="font-medium text-slate-700">
              {termo ? "Nenhuma pessoa encontrada." : "Nenhuma pessoa cadastrada ainda."}
            </p>
            <p className="mt-1 text-slate-500">
              {termo
                ? "Tente outro nome ou documento."
                : "Comece cadastrando o primeiro cliente do escritório."}
            </p>
          </div>
        )}

        {pessoas.data && pessoas.data.itens.length > 0 && (
          <>
            <div className="overflow-x-auto rounded-[--radius-cartao] border border-borda bg-superficie shadow-nivel-1">
              <table className="w-full min-w-4xl border-collapse text-left">
                <thead>
                  <tr className="border-b border-borda text-xs tracking-wide text-slate-500 uppercase">
                    {colunas.map((coluna) => (
                      <th key={coluna.chave} scope="col" className="px-4 py-3 font-semibold">
                        {coluna.titulo}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {pessoas.data.itens.map((pessoa) => (
                    <tr key={pessoa.id} className="border-b border-borda last:border-0 hover:bg-marca-50">
                      {colunas.map((coluna, indice) => (
                        <td key={coluna.chave} className={`px-4 py-3 ${coluna.classe ?? ""}`}>
                          {/*
                            A primeira coluna visível abre o cadastro, seja ela
                            qual for. Prender o link ao nome deixaria a linha
                            sem caminho de entrada para quem escondesse essa
                            coluna — e esconder qualquer uma é o ponto do
                            configurador.
                          */}
                          {indice === 0 ? (
                            <>
                              <Link
                                href={`/pessoas/${pessoa.id}`}
                                className="font-medium text-marca-700 hover:underline"
                              >
                                {coluna.conteudo(pessoa)}
                              </Link>
                              {!pessoa.ativo && (
                                <span className="ml-2 rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                                  inativa
                                </span>
                              )}
                            </>
                          ) : (
                            coluna.conteudo(pessoa)
                          )}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <Paginacao
              pagina={pessoas.data.pagina}
              tamanho={pessoas.data.tamanho}
              total={pessoas.data.total}
              aoMudar={definirPagina}
              substantivo={{ singular: "pessoa encontrada", plural: "pessoas encontradas" }}
            />
          </>
        )}
      </div>
    </>
  );
}
