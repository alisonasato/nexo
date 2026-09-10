"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import { Botao } from "@/componentes/controles";
import { Paginacao } from "@/componentes/paginacao";
import { formatarDocumento, formatarTelefone } from "@/lib/formato";

export default function ListagemDePessoas() {
  const [busca, definirBusca] = useState("");
  const [termo, definirTermo] = useState("");
  const [pagina, definirPagina] = useState(1);

  /*
   * Inativar pede o segundo clique.
   *
   * É reversível no dado — a pessoa continua no banco —, mas não pela tela: não
   * há como reativar por aqui ainda. Um clique só, sem confirmação e sem
   * desfazer, tira um cliente da lista por engano de mira. O botão vira
   * "Confirmar?" e volta ao normal em cinco segundos.
   */
  const [confirmando, definirConfirmando] = useState<string | null>(null);

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
    queryKey: ["pessoas", termo, pagina],
    queryFn: async () => {
      const { data, error } = await api.GET("/pessoas", {
        params: { query: { pagina, ...(termo ? { busca: termo } : {}) } },
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
      if (error) throw new Error("Não foi possível inativar este cadastro.");
    },
    onSuccess: () => {
      definirConfirmando(null);
      clienteDeConsultas.invalidateQueries({ queryKey: ["pessoas"] });
    },
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
          {pessoas.isFetching && <span className="text-xs text-slate-500">buscando…</span>}
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
              <table className="w-full min-w-3xl border-collapse text-left">
                <thead>
                  <tr className="border-b border-borda text-xs tracking-wide text-slate-500 uppercase">
                    <th scope="col" className="px-4 py-3 font-semibold">Nome</th>
                    <th scope="col" className="px-4 py-3 font-semibold">Documento</th>
                    <th scope="col" className="px-4 py-3 font-semibold">Contato</th>
                    <th scope="col" className="px-4 py-3 font-semibold">Cidade</th>
                    <th scope="col" className="px-4 py-3 font-semibold">
                      <span className="sr-only">Ações</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {pessoas.data.itens.map((pessoa) => (
                    <tr key={pessoa.id} className="border-b border-borda last:border-0 hover:bg-marca-50">
                      <td className="px-4 py-3">
                        <Link
                          href={`/pessoas/${pessoa.id}`}
                          className="font-medium text-marca-700 hover:underline"
                        >
                          {pessoa.nome}
                        </Link>
                        {pessoa.nomeFantasia && (
                          <span className="block text-slate-500">{pessoa.nomeFantasia}</span>
                        )}
                      </td>
                      <td className="numeros-tabulares px-4 py-3 text-slate-700">
                        {formatarDocumento(pessoa.documento)}
                      </td>
                      <td className="px-4 py-3 text-slate-700">
                        {pessoa.email || formatarTelefone(pessoa.celular) || "—"}
                      </td>
                      <td className="px-4 py-3 text-slate-700">
                        {pessoa.cidade ? `${pessoa.cidade}${pessoa.uf ? "/" + pessoa.uf : ""}` : "—"}
                      </td>
                      <td className="px-4 py-3 text-right">
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
