"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import { Botao } from "@/componentes/controles";
import { Paginacao } from "@/componentes/paginacao";
import type { components } from "@/api/esquema";
import { formatarDocumento, formatarEndereco, formatarTelefone } from "@/lib/formato";

type Papel = components["schemas"]["Papel"];
type Problema = components["schemas"]["Problema"];

/**
 * Os problemas que vieram no 422, ou lista vazia.
 *
 * O openapi-fetch entrega o corpo do erro como `unknown`, então a checagem é
 * de forma, não de tipo — e um erro de rede, que não tem corpo nenhum, cai no
 * mesmo caminho sem quebrar.
 */
function problemasDaResposta(erro: unknown): Problema[] {
  if (erro && typeof erro === "object" && "problemas" in erro && Array.isArray(erro.problemas)) {
    return erro.problemas as Problema[];
  }
  return [];
}

const papeis: Papel[] = ["Cliente", "Fornecedor", "Vendedor", "Colaborador"];

export default function ListagemDePessoas() {
  const [busca, definirBusca] = useState("");
  const [termo, definirTermo] = useState("");
  const [pagina, definirPagina] = useState(1);
  const [incluirInativas, definirIncluirInativas] = useState(false);
  const [papel, definirPapel] = useState<Papel | "">("");

  /*
   * Inativar pede o segundo clique; reativar não.
   *
   * A assimetria é de propósito. Inativar some com a pessoa da lista, e um
   * clique de mira errada faria isso sem aviso — o botão vira "Confirmar?" e
   * volta ao normal em cinco segundos. Reativar só devolve o que já existia, e
   * quem errar o alvo desfaz com um clique. Pedir confirmação nos dois lados
   * ensinaria a clicar duas vezes sem ler, que é como confirmação vira ruído.
   */
  const [confirmando, definirConfirmando] = useState<string | null>(null);

  /*
   * O motivo da recusa fica na tela até a próxima tentativa. Some sozinho
   * seria pior: a mensagem diz qual contrato encerrar, e quem foi conferir
   * isso em outra aba volta e precisa dela ainda ali.
   */
  const [impedimentos, definirImpedimentos] = useState<string[]>([]);

  useEffect(() => {
    if (!confirmando) return;
    const relogio = setTimeout(() => definirConfirmando(null), 5000);
    return () => clearTimeout(relogio);
  }, [confirmando]);
  const clienteDeConsultas = useQueryClient();

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

  const inativar = useMutation({
    mutationFn: async (id: string) => {
      const { error } = await api.DELETE("/pessoas/{id}", { params: { path: { id } } });
      if (error) throw error;
    },
    onMutate: () => definirImpedimentos([]),
    onSuccess: () => {
      definirConfirmando(null);
      clienteDeConsultas.invalidateQueries({ queryKey: ["pessoas"] });
    },
    onError: (erro: unknown) => {
      /* O botão volta a "Inativar": insistir no mesmo clique não muda nada. */
      definirConfirmando(null);

      const problemas = problemasDaResposta(erro);
      definirImpedimentos(
        problemas.length > 0
          ? problemas.map((problema) => `${problema.descricao} ${problema.sugestao}`)
          : ["Não foi possível inativar este cadastro. Tente de novo em instantes."],
      );
    },
  });

  const reativar = useMutation({
    mutationFn: async (id: string) => {
      const { error } = await api.POST("/pessoas/{id}/reativar", { params: { path: { id } } });
      if (error) throw new Error("Não foi possível reativar este cadastro.");
    },
    onSuccess: () => clienteDeConsultas.invalidateQueries({ queryKey: ["pessoas"] }),
  });

  return (
    <>
      <header className="flex flex-wrap items-center justify-between gap-4 border-b border-borda bg-superficie px-6 py-4">
        <div>
          <h1 className="text-xl font-semibold tracking-tight text-marca-950">Pessoas</h1>
          <p className="text-slate-500">Clientes, fornecedores e demais cadastros do escritório.</p>
        </div>

        <Link
          href="/pessoas/novo"
          className="inline-flex items-center rounded-[--radius-controle] bg-marca-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-marca-700"
        >
          Nova pessoa
        </Link>
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

        {impedimentos.length > 0 && (
          <div
            role="alert"
            className="rounded-[--radius-controle] border border-amber-200 bg-amber-50 px-4 py-3 text-amber-900"
          >
            <p className="font-medium">Este cadastro ainda está em uso.</p>
            <ul className="mt-1 list-disc space-y-1 pl-5">
              {impedimentos.map((motivo) => (
                <li key={motivo}>{motivo}</li>
              ))}
            </ul>
          </div>
        )}

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
                    <th scope="col" className="px-4 py-3 font-semibold">Código</th>
                    <th scope="col" className="px-4 py-3 font-semibold">Nome</th>
                    <th scope="col" className="px-4 py-3 font-semibold">Documento</th>
                    <th scope="col" className="px-4 py-3 font-semibold">Endereço</th>
                    <th scope="col" className="px-4 py-3 font-semibold">Contato</th>
                    <th scope="col" className="px-4 py-3 font-semibold">
                      <span className="sr-only">Ações</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {pessoas.data.itens.map((pessoa) => (
                    <tr key={pessoa.id} className="border-b border-borda last:border-0 hover:bg-marca-50">
                      <td className="numeros-tabulares px-4 py-3 font-medium text-slate-800">
                        {pessoa.codigo}
                      </td>
                      <td className="px-4 py-3">
                        <Link
                          href={`/pessoas/${pessoa.id}`}
                          className="font-medium text-marca-700 hover:underline"
                        >
                          {pessoa.nome}
                        </Link>
                        {!pessoa.ativo && (
                          <span className="ml-2 rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                            inativa
                          </span>
                        )}

                        {pessoa.nomeFantasia && (
                          <span className="block text-slate-500">{pessoa.nomeFantasia}</span>
                        )}
                      </td>
                      <td className="numeros-tabulares px-4 py-3 text-slate-700">
                        {formatarDocumento(pessoa.documento)}
                      </td>
                      <td className="px-4 py-3 text-slate-700">
                        {formatarEndereco(pessoa.endereco) || "—"}
                      </td>

                      {/*
                        O fixo na frente do celular, e o celular quando não há
                        fixo. Cadastro com um número só é o caso comum, e uma
                        coluna vazia ao lado de um telefone gravado seria um
                        defeito com cara de dado faltando.
                      */}
                      <td className="numeros-tabulares px-4 py-3 text-slate-700">
                        {formatarTelefone(pessoa.telefone || pessoa.celular) || "—"}
                      </td>
                      <td className="px-4 py-3 text-right">
                        {pessoa.ativo ? (
                          <Botao
                            aparencia={confirmando === pessoa.id ? "perigo" : "secundario"}
                            type="button"
                            disabled={inativar.isPending}
                            onClick={() =>
                              confirmando === pessoa.id
                                ? inativar.mutate(pessoa.id)
                                : definirConfirmando(pessoa.id)
                            }
                            className="px-3 py-1 text-xs"
                          >
                            {confirmando === pessoa.id ? "Confirmar?" : "Inativar"}
                          </Botao>
                        ) : (
                          <Botao
                            aparencia="secundario"
                            type="button"
                            disabled={reativar.isPending}
                            onClick={() => reativar.mutate(pessoa.id)}
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
