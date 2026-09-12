"use client";

import Link from "next/link";
import { useEffect, useState, useSyncExternalStore } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import { Paginacao } from "@/componentes/paginacao";
import { GerenciadorDeColunas } from "@/componentes/gerenciador-de-colunas";
import { MenuDeAcoes, type AcaoDeLinha } from "@/componentes/menu-de-acoes";
import {
  DesenhoDeCadastroVazio,
  IconeDeAbrir,
  IconeDeBusca,
  IconeDeColunas,
  IconeDeCopiar,
  IconeDeFiltro,
  IconeDeSeta,
} from "@/componentes/icones";
import type { components } from "@/api/esquema";
import { formatarDocumento } from "@/lib/formato";
import {
  assinarEscolhas,
  classeDeAlinhamento,
  colunasVisiveis,
  escolhasEmUso,
  escolhasNoServidor,
  gravarEscolhas,
} from "./colunas";

type Papel = components["schemas"]["Papel"];
type PessoaNaLista = components["schemas"]["PessoaNaLista"];

const papeis: Papel[] = ["Cliente", "Fornecedor", "Vendedor", "Colaborador"];

export default function ListagemDePessoas() {
  const [busca, definirBusca] = useState("");
  const [termo, definirTermo] = useState("");
  const [pagina, definirPagina] = useState(1);
  const [incluirInativas, definirIncluirInativas] = useState(false);
  const [papel, definirPapel] = useState<Papel | "">("");
  const [configurando, definirConfigurando] = useState(false);
  const [filtrosAbertos, definirFiltrosAbertos] = useState(false);

  const escolhas = useSyncExternalStore(assinarEscolhas, escolhasEmUso, escolhasNoServidor);
  const colunas = colunasVisiveis(escolhas);

  /*
   * A listagem não inativa ninguém, de propósito. Inativar exige olhar o
   * cadastro — quantos contratos a pessoa tem, o que ainda deve — e essa
   * conferência não cabe numa linha de tabela. Por isso o menu de três pontos
   * não a traz de volta: ele guarda o que se resolve sem sair daqui.
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

  /*
   * Quantos filtros estão valendo, para o número aparecer no botão.
   *
   * É o que permite recolher os filtros sem escondê-los: sem essa contagem,
   * fechar o painel apagaria da tela a informação de que a lista está
   * recortada, e o próximo a olhar juraria que o cadastro encolheu.
   */
  const filtrosAtivos = (papel ? 1 : 0) + (incluirInativas ? 1 : 0);
  const filtrandoAlgo = filtrosAtivos > 0 || termo.length > 0;

  function limparFiltros() {
    definirPapel("");
    definirIncluirInativas(false);
    definirBusca("");
    definirPagina(1);
  }

  function acoesDaLinha(pessoa: PessoaNaLista): AcaoDeLinha[] {
    const acoes: AcaoDeLinha[] = [
      {
        tipo: "link",
        rotulo: "Abrir cadastro",
        href: `/pessoas/${pessoa.id}`,
        icone: <IconeDeAbrir className="size-4" />,
      },
    ];

    /*
     * Copiar o documento é a ação mais repetida de um escritório de
     * contabilidade: ele é digitado de novo em cada portal, e cada redigitação
     * é uma chance de trocar um dígito.
     */
    if (pessoa.documento) {
      acoes.push({
        tipo: "botao",
        rotulo: "Copiar CPF/CNPJ",
        icone: <IconeDeCopiar className="size-4" />,
        executar: async () => {
          try {
            await navigator.clipboard.writeText(formatarDocumento(pessoa.documento));
            return "CPF/CNPJ copiado";
          } catch {
            /* Sem permissão de área de transferência, ou navegador antigo. */
            return "Não foi possível copiar";
          }
        },
      });
    }

    return acoes;
  }

  return (
    <>
      {/*
        Cabeçalho: título, uma linha dizendo o que a tela é, e a ação principal
        no canto superior direito — canto, e não meio, porque a borda da tela
        segura o ponteiro e o alvo fica maior do que o desenho dele.
      */}
      <header className="flex flex-wrap items-start justify-between gap-4 border-b border-borda bg-superficie px-6 py-5 sm:px-8">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight text-marca-950">Pessoas</h1>
          <p className="mt-1 text-slate-500">
            Clientes, fornecedores e demais cadastros do escritório.
          </p>
        </div>

        <Link
          href="/pessoas/novo"
          className="inline-flex min-h-11 w-full items-center justify-center rounded-[--radius-controle] bg-marca-600 px-4 text-sm font-semibold text-white transition-colors hover:bg-marca-700 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-marca-600 sm:w-auto"
        >
          Nova pessoa
        </Link>
      </header>

      <div className="flex flex-1 flex-col gap-5 p-6 sm:p-8">
        {/* Busca e filtros num bloco só: são a mesma pergunta feita de dois jeitos. */}
        <section
          aria-label="Busca e filtros"
          className="rounded-[--radius-cartao] border border-borda bg-superficie p-4 shadow-nivel-1"
        >
          <div className="flex flex-wrap items-center gap-3">
            {/* No celular a busca fica com a linha inteira: dividindo espaço com
                dois botões ela sobrava com 134px, e o texto digitado não cabia. */}
            <div className="relative min-w-0 basis-full sm:flex-1 sm:basis-auto">
              <IconeDeBusca className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-slate-400" />
              <input
                type="search"
                value={busca}
                onChange={(evento) => definirBusca(evento.target.value)}
                placeholder="Buscar por nome, nome fantasia ou documento"
                aria-label="Buscar pessoas"
                className="min-h-11 w-full rounded-[--radius-controle] border border-borda-forte bg-superficie py-2.5 pr-24 pl-9 placeholder:text-slate-400 focus:border-marca-500 focus-visible:outline-none"
              />

              {/* Dentro do campo, onde os olhos já estão enquanto se digita. */}
              {pessoas.isFetching && (
                <span
                  role="status"
                  className="absolute top-1/2 right-3 -translate-y-1/2 text-xs text-slate-500"
                >
                  buscando…
                </span>
              )}
            </div>

            <button
              type="button"
              onClick={() => definirFiltrosAbertos((aberto) => !aberto)}
              aria-expanded={filtrosAbertos}
              className="inline-flex min-h-11 shrink-0 items-center gap-2 rounded-[--radius-controle] border border-borda-forte bg-superficie px-3 text-sm font-semibold text-slate-700 transition-colors hover:bg-slate-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-marca-500"
            >
              <IconeDeFiltro />
              Filtros
              {filtrosAtivos > 0 && (
                <span className="rounded-full bg-marca-600 px-1.5 text-xs font-semibold text-white">
                  {filtrosAtivos}
                </span>
              )}
              <IconeDeSeta
                className={
                  "size-4 transition-transform duration-200 " + (filtrosAbertos ? "rotate-180" : "")
                }
              />
            </button>

            <div className="relative shrink-0">
              <button
                type="button"
                onClick={() => definirConfigurando((aberto) => !aberto)}
                aria-expanded={configurando}
                aria-haspopup="dialog"
                className="inline-flex min-h-11 items-center gap-2 rounded-[--radius-controle] border border-borda-forte bg-superficie px-3 text-sm font-semibold text-slate-700 transition-colors hover:bg-slate-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-marca-500"
              >
                <IconeDeColunas />
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
            </div>
          </div>

          {filtrosAbertos && (
            <div className="mt-4 flex flex-wrap items-center gap-x-6 gap-y-3 border-t border-borda pt-4">
              <div className="flex flex-wrap items-center gap-2">
                <span className="text-sm font-medium text-slate-600">Papel</span>
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
                      "inline-flex min-h-9 items-center rounded-full px-3.5 text-sm font-medium transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-marca-500 " +
                      (papel === valor
                        ? "bg-marca-600 text-white"
                        : "border border-borda-forte bg-superficie text-slate-700 hover:bg-slate-50")
                    }
                  >
                    {valor || "Todos"}
                  </button>
                ))}
              </div>

              <label className="flex items-center gap-2 text-slate-600">
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

              {filtrandoAlgo && (
                <button
                  type="button"
                  onClick={limparFiltros}
                  className="text-sm font-semibold text-marca-700 underline-offset-2 hover:underline"
                >
                  Limpar filtros
                </button>
              )}
            </div>
          )}
        </section>

        {pessoas.isError && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            {pessoas.error.message} Verifique se a API está no ar e tente de novo.
          </p>
        )}

        {/*
          Esqueleto, e não a palavra "carregando". Ele já ocupa a altura que a
          tabela vai ocupar, então o conteúdo chega no lugar em vez de empurrar
          o resto da página para baixo.
        */}
        {pessoas.isPending && (
          <div
            role="status"
            aria-label="Carregando o cadastro"
            className="overflow-hidden rounded-[--radius-cartao] border border-borda bg-superficie shadow-nivel-1"
          >
            {Array.from({ length: 8 }).map((_, linha) => (
              <div key={linha} className="flex items-center gap-4 border-b border-borda px-4 py-4 last:border-0">
                <div className="h-3 w-10 animate-pulse rounded bg-slate-100" />
                <div className="h-3 flex-1 animate-pulse rounded bg-slate-100" />
                <div className="h-3 w-32 animate-pulse rounded bg-slate-100" />
                <div className="h-3 w-24 animate-pulse rounded bg-slate-100" />
              </div>
            ))}
          </div>
        )}

        {pessoas.data && pessoas.data.itens.length === 0 && (
          <div className="flex flex-col items-center rounded-[--radius-cartao] border border-dashed border-borda-forte bg-superficie px-6 py-16 text-center">
            <DesenhoDeCadastroVazio className="size-14 text-slate-300" />

            <p className="mt-4 text-lg font-semibold text-slate-800">
              {filtrandoAlgo ? "Nenhuma pessoa com esses filtros." : "O cadastro ainda está vazio."}
            </p>

            <p className="mt-1 max-w-sm text-slate-500">
              {filtrandoAlgo
                ? "Talvez o termo esteja diferente do que foi cadastrado, ou a pessoa esteja inativa."
                : "Cadastre o primeiro cliente do escritório para começar a montar contratos e cobranças."}
            </p>

            <div className="mt-6 flex flex-wrap justify-center gap-3">
              {filtrandoAlgo && (
                <button
                  type="button"
                  onClick={limparFiltros}
                  className="rounded-[--radius-controle] border border-borda-forte px-4 py-2 text-sm font-semibold text-slate-700 transition-colors hover:bg-slate-50"
                >
                  Limpar filtros
                </button>
              )}

              <Link
                href="/pessoas/novo"
                className="rounded-[--radius-controle] bg-marca-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-marca-700"
              >
                Nova pessoa
              </Link>
            </div>
          </div>
        )}

        {pessoas.data && pessoas.data.itens.length > 0 && (
          <>
            <div className="overflow-x-auto rounded-[--radius-cartao] border border-borda bg-superficie shadow-nivel-1">
              <table className="w-full min-w-4xl border-collapse">
                <thead>
                  <tr className="border-b border-borda bg-slate-50/60 text-xs tracking-wide text-slate-500 uppercase">
                    {colunas.map((coluna) => (
                      <th
                        key={coluna.chave}
                        scope="col"
                        className={`px-4 py-3 font-semibold ${classeDeAlinhamento(coluna)}`}
                      >
                        {coluna.titulo}
                      </th>
                    ))}
                    <th scope="col" className="w-px px-4 py-3">
                      <span className="sr-only">Ações</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {pessoas.data.itens.map((pessoa) => (
                    <tr
                      key={pessoa.id}
                      className="border-b border-borda transition-colors last:border-0 hover:bg-marca-50/60"
                    >
                      {colunas.map((coluna, indice) => (
                        <td
                          key={coluna.chave}
                          className={`px-4 py-4 ${classeDeAlinhamento(coluna)} ${coluna.classe ?? ""}`}
                        >
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
                                className="font-medium text-marca-700 hover:underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-marca-500"
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

                      <td className="px-2 py-2">
                        <MenuDeAcoes rotulo={pessoa.nome} acoes={acoesDaLinha(pessoa)} />
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
