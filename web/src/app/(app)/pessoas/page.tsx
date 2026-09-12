"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState, useSyncExternalStore } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import { Paginacao } from "@/componentes/paginacao";
import { GerenciadorDeColunas } from "@/componentes/gerenciador-de-colunas";
import { MenuDeAcoes, type AcaoDeLinha } from "@/componentes/menu-de-acoes";
import { BarraDeSelecao } from "@/componentes/barra-de-selecao";
import { VoltarAoTopo } from "@/componentes/voltar-ao-topo";
import { useAvisos } from "@/componentes/avisos";
import {
  DesenhoDeCadastroVazio,
  IconeDeAbrir,
  IconeDeBusca,
  IconeDeColunas,
  IconeDeCopiar,
  IconeDeFiltro,
  IconeDeOrdenacao,
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
import { useSelecaoPorConsulta } from "./selecao";

type Papel = components["schemas"]["Papel"];
type PessoaNaLista = components["schemas"]["PessoaNaLista"];
type Problema = components["schemas"]["Problema"];
type OrdemDaListagem = components["schemas"]["OrdemDaListagem"];
type Direcao = components["schemas"]["Direcao"];

const papeis: Papel[] = ["Cliente", "Fornecedor", "Vendedor", "Colaborador"];

/** "1 inativado" e "3 inativados": o número manda no participio. */
const concordar = (quantos: number, participio: string) =>
  `${quantos} ${participio}${quantos === 1 ? "" : "s"}`;

function problemasDaResposta(erro: unknown): Problema[] {
  if (erro && typeof erro === "object" && "problemas" in erro && Array.isArray(erro.problemas)) {
    return erro.problemas as Problema[];
  }
  return [];
}

export default function ListagemDePessoas() {
  const navegacao = useRouter();
  const avisar = useAvisos();

  const [busca, definirBusca] = useState("");
  const [termo, definirTermo] = useState("");
  const [pagina, definirPagina] = useState(1);
  const [tamanho, definirTamanho] = useState(25);
  const [incluirInativas, definirIncluirInativas] = useState(false);
  const [papel, definirPapel] = useState<Papel | "">("");
  const [configurando, definirConfigurando] = useState(false);
  const [filtrosAbertos, definirFiltrosAbertos] = useState(false);
  const [inativando, definirInativando] = useState(false);

  /*
   * A ordem vai para a API, e não para um `sort` aqui.
   *
   * Ordenar no navegador ordenaria a página, não a lista: com 25 de 300, o
   * resultado seria uma ordem perfeita dentro de um recorte arbitrário, e a
   * página 2 traria nomes que deviam vir antes dos da página 1. Plausível e
   * errado, que é o jeito mais caro de errar.
   */
  const [ordem, definirOrdem] = useState<{ por: OrdemDaListagem; direcao: Direcao }>({
    por: "Nome",
    direcao: "Crescente",
  });

  const escolhas = useSyncExternalStore(assinarEscolhas, escolhasEmUso, escolhasNoServidor);
  const colunas = colunasVisiveis(escolhas);

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

  const consulta = { termo, papel, pagina, tamanho, incluirInativas, ordem };

  const pessoas = useQuery({
    queryKey: ["pessoas", consulta],
    queryFn: async () => {
      const { data, error } = await api.GET("/pessoas", {
        params: {
          query: {
            pagina,
            tamanho,
            ordenarPor: ordem.por,
            direcao: ordem.direcao,
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

  const selecao = useSelecaoPorConsulta(JSON.stringify(consulta));
  const itens = pessoas.data?.itens ?? [];

  const filtrosAtivos = (papel ? 1 : 0) + (incluirInativas ? 1 : 0);
  const filtrandoAlgo = filtrosAtivos > 0 || termo.length > 0;

  function limparFiltros() {
    definirPapel("");
    definirIncluirInativas(false);
    definirBusca("");
    definirPagina(1);
  }

  const abrir = (id: string) => navegacao.push(`/pessoas/${id}`);

  /**
   * Clicar no cabeçalho ordena por ele; clicar de novo inverte.
   *
   * Coluna nova começa sempre crescente. Herdar a direção da coluna anterior
   * faria o primeiro clique num cabeçalho devolver a ordem de trás para a
   * frente, que ninguém pede ao clicar pela primeira vez.
   */
  function ordenarPor(por: OrdemDaListagem) {
    definirOrdem((atual) =>
      atual.por === por
        ? { por, direcao: atual.direcao === "Crescente" ? "Decrescente" : "Crescente" }
        : { por, direcao: "Crescente" },
    );

    /* A página 3 da ordem antiga não é a página 3 da nova. */
    definirPagina(1);
    selecao.limpar();
  }

  /**
   * Inativa o que está marcado, uma requisição por cadastro.
   *
   * Não existe rota de lote na API, e inventar uma aqui esconderia o que
   * interessa: a inativação é recusada quando a pessoa ainda tem contrato não
   * encerrado ou cobrança em aberto. Numa seleção de cinco, três podem passar e
   * duas serem recusadas — e o aviso precisa dizer isso, com o motivo, em vez
   * de um "erro ao excluir" que não ajuda ninguém.
   */
  async function inativarSelecionadas() {
    const alvos = itens.filter((pessoa) => selecao.marcadas.has(pessoa.id));
    if (alvos.length === 0) return;

    const pergunta =
      alvos.length === 1 ? `Inativar “${alvos[0].nome}”?` : `Inativar ${alvos.length} cadastros?`;

    if (!window.confirm(`${pergunta}\n\nEles saem da listagem, mas continuam no histórico.`)) {
      return;
    }

    definirInativando(true);

    const recusas: string[] = [];
    let feitas = 0;

    for (const pessoa of alvos) {
      const { error } = await api.DELETE("/pessoas/{id}", { params: { path: { id: pessoa.id } } });

      if (!error) {
        feitas += 1;
        continue;
      }

      const problemas = problemasDaResposta(error);
      recusas.push(problemas[0]?.descricao ?? `“${pessoa.nome}” não pôde ser inativada.`);
    }

    definirInativando(false);
    selecao.limpar();
    pessoas.refetch();

    if (recusas.length === 0) {
      avisar({
        tom: "sucesso",
        titulo: feitas === 1 ? "Cadastro inativado." : `${feitas} cadastros inativados.`,
      });
      return;
    }

    avisar({
      tom: feitas === 0 ? "erro" : "informacao",
      titulo:
        feitas === 0
          ? recusas.length === 1
            ? "O cadastro não pôde ser inativado."
            : `Nenhum dos ${recusas.length} cadastros pôde ser inativado.`
          : `${concordar(feitas, "inativado")}, ${concordar(recusas.length, "recusado")}.`,
      /* O primeiro motivo por extenso: é o que diz o que fazer a seguir. */
      detalhe: recusas[0],
    });
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
            return "Não foi possível copiar";
          }
        },
      });
    }

    return acoes;
  }

  /*
   * Clique na linha marca; clique no que já é controle, não.
   *
   * Sem esta checagem, abrir o menu de três pontos ou seguir o link do nome
   * marcaria a linha de brinde. O alvo do evento decide: se veio de dentro de
   * um link, botão ou campo, a linha não se mete.
   */
  function cliqueVeioDeControle(evento: React.MouseEvent) {
    return Boolean((evento.target as HTMLElement).closest("a,button,input,select,label"));
  }

  /*
   * Quem anuncia "marcada" é a caixa de marcação, e não a linha.
   * `aria-selected` só é entendido em linha de `role="grid"`, e numa tabela
   * comum ele é inventar semântica que nenhum leitor de tela vai ler.
   */

  const todasMarcadas = itens.length > 0 && selecao.quantidade === itens.length;
  const algumaMarcada = selecao.quantidade > 0;

  return (
    <>
      <header className="flex flex-wrap items-start justify-between gap-4 border-b border-borda bg-superficie px-6 py-5 sm:px-8">
        <div>
          <h1 id="conteudo" tabIndex={-1} className="text-2xl font-semibold tracking-tight text-marca-950">
            Pessoas
          </h1>
          <p className="mt-1 text-slate-600">
            Clientes, fornecedores e demais cadastros do escritório.
          </p>
        </div>

        <Link
          href="/pessoas/novo"
          className="inline-flex min-h-11 w-full items-center justify-center rounded-[--radius-controle] bg-marca-600 px-4 text-sm font-semibold text-white transition-colors hover:bg-marca-700 sm:w-auto"
        >
          Nova pessoa
        </Link>
      </header>

      <div className="flex flex-1 flex-col gap-5 p-6 sm:p-8">
        <section
          aria-label="Busca e filtros"
          className="rounded-[--radius-cartao] border border-borda bg-superficie p-4 shadow-nivel-1"
        >
          <div className="flex flex-wrap items-center gap-3">
            <div className="relative min-w-0 basis-full sm:flex-1 sm:basis-auto">
              <IconeDeBusca className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-slate-400" />
              <input
                type="search"
                value={busca}
                onChange={(evento) => definirBusca(evento.target.value)}
                placeholder="Buscar por nome, nome fantasia ou documento"
                aria-label="Buscar pessoas"
                className="min-h-11 w-full rounded-[--radius-controle] border border-borda-forte bg-superficie py-2.5 pr-24 pl-9 placeholder:text-slate-400 focus:border-marca-500"
              />

              {pessoas.isFetching && (
                <span
                  role="status"
                  className="absolute top-1/2 right-3 -translate-y-1/2 text-xs text-slate-600"
                >
                  buscando…
                </span>
              )}
            </div>

            <button
              type="button"
              onClick={() => definirFiltrosAbertos((aberto) => !aberto)}
              aria-expanded={filtrosAbertos}
              className="inline-flex min-h-11 shrink-0 items-center gap-2 rounded-[--radius-controle] border border-borda-forte bg-superficie px-3 text-sm font-semibold text-slate-700 transition-colors hover:bg-slate-50"
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
                  "size-4 motion-safe:transition-transform motion-safe:duration-200 " +
                  (filtrosAbertos ? "rotate-180" : "")
                }
              />
            </button>

            <div className="relative shrink-0">
              <button
                type="button"
                onClick={() => definirConfigurando((aberto) => !aberto)}
                aria-expanded={configurando}
                aria-haspopup="dialog"
                className="inline-flex min-h-11 items-center gap-2 rounded-[--radius-controle] border border-borda-forte bg-superficie px-3 text-sm font-semibold text-slate-700 transition-colors hover:bg-slate-50"
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
                    avisar({ tom: "sucesso", titulo: "Colunas atualizadas." });
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
                      "inline-flex min-h-9 items-center rounded-full px-3.5 text-sm font-medium transition-colors " +
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

        {pessoas.isPending && <Esqueleto colunas={colunas.length} />}

        {pessoas.data && itens.length === 0 && (
          <div className="flex flex-col items-center rounded-[--radius-cartao] border border-dashed border-borda-forte bg-superficie px-6 py-16 text-center">
            <DesenhoDeCadastroVazio className="size-14 text-slate-300" />

            <p className="mt-4 text-lg font-semibold text-slate-800">
              {filtrandoAlgo ? "Nenhuma pessoa com esses filtros." : "O cadastro ainda está vazio."}
            </p>

            <p className="mt-1 max-w-sm text-slate-600">
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

        {pessoas.data && itens.length > 0 && (
          <>
            {/* Cartões no celular; tabela a partir de 768px. */}
            <ul className="flex flex-col gap-3 md:hidden">
              {itens.map((pessoa) => {
                const marcada = selecao.marcadas.has(pessoa.id);

                return (
                  <li key={pessoa.id}>
                    <article
                      onClick={(evento) => {
                        if (cliqueVeioDeControle(evento)) return;
                        selecao.alternar(pessoa.id);
                      }}
                      onDoubleClick={() => abrir(pessoa.id)}
                      className={
                        /*
                          Sobe dois pixels por transform, que não reflui: o
                          cartão de baixo não se mexe junto. Só sobe o que é
                          clicável — cartão que levanta sem levar a lugar
                          nenhum promete uma ação que não existe.
                        */
                        "cursor-pointer rounded-[--radius-cartao] border p-4 transition-all hover:-translate-y-0.5 " +
                        (marcada
                          ? "border-marca-400 bg-marca-50 shadow-nivel-2"
                          : "border-borda bg-superficie shadow-nivel-1 hover:shadow-nivel-2")
                      }
                    >
                      <div className="flex items-start gap-3">
                        <input
                          type="checkbox"
                          checked={marcada}
                          onChange={() => selecao.alternar(pessoa.id)}
                          aria-label={`Selecionar ${pessoa.nome}`}
                          className={
                            "mt-1 size-5 shrink-0 rounded border-borda-forte accent-marca-600 transition-opacity " +
                            (algumaMarcada ? "opacity-100" : "opacity-40")
                          }
                        />

                        <div className="min-w-0 flex-1">
                          <Link
                            href={`/pessoas/${pessoa.id}`}
                            className="font-semibold text-marca-700 hover:underline"
                          >
                            {pessoa.nome}
                          </Link>

                          {!pessoa.ativo && (
                            <span className="ml-2 rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                              inativa
                            </span>
                          )}

                          <dl className="mt-2 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
                            {colunas
                              .filter((coluna) => coluna.chave !== "nome")
                              .map((coluna) => (
                                <div key={coluna.chave} className="contents">
                                  <dt className="text-xs tracking-wide text-slate-400 uppercase">
                                    {coluna.titulo}
                                  </dt>
                                  <dd className={`min-w-0 text-slate-700 ${coluna.classe ?? ""}`}>
                                    {coluna.conteudo(pessoa)}
                                  </dd>
                                </div>
                              ))}
                          </dl>
                        </div>

                        <MenuDeAcoes rotulo={pessoa.nome} acoes={acoesDaLinha(pessoa)} />
                      </div>
                    </article>
                  </li>
                );
              })}
            </ul>

            <div className="hidden overflow-x-auto rounded-[--radius-cartao] border border-borda bg-superficie shadow-nivel-1 md:block">
              <table className="w-full min-w-3xl border-collapse">
                <thead>
                  <tr className="border-b border-borda bg-slate-50/60 text-xs tracking-wide text-slate-500 uppercase">
                    <th scope="col" className="w-px pr-2 pl-4">
                      <input
                        type="checkbox"
                        checked={todasMarcadas}
                        ref={(caixa) => {
                          if (caixa) caixa.indeterminate = algumaMarcada && !todasMarcadas;
                        }}
                        onChange={() => selecao.trocarTodas(itens.map((pessoa) => pessoa.id))}
                        aria-label="Selecionar todas as pessoas desta página"
                        className={
                          "size-4 rounded border-borda-forte accent-marca-600 transition-opacity " +
                          (algumaMarcada ? "opacity-100" : "opacity-30")
                        }
                      />
                    </th>

                    {colunas.map((coluna) => {
                      const ativa = coluna.ordenarPor === ordem.por;

                      return (
                        <th
                          key={coluna.chave}
                          scope="col"
                          /*
                            `aria-sort` é o que faz um leitor de tela anunciar
                            "ordenado de forma crescente" ao chegar na coluna.
                            Sem ele, a seta é informação só para quem enxerga.
                          */
                          aria-sort={
                            !coluna.ordenarPor || !ativa
                              ? undefined
                              : ordem.direcao === "Crescente"
                                ? "ascending"
                                : "descending"
                          }
                          className={`font-semibold ${classeDeAlinhamento(coluna)} ${coluna.ordenarPor ? "p-0" : "px-4 py-3"}`}
                        >
                          {coluna.ordenarPor ? (
                            <button
                              type="button"
                              onClick={() => ordenarPor(coluna.ordenarPor!)}
                              className={
                                "inline-flex w-full cursor-pointer items-center gap-1.5 px-4 py-3 text-xs tracking-wide uppercase transition-colors hover:bg-slate-100 " +
                                (coluna.alinhamento === "direita"
                                  ? "justify-end"
                                  : coluna.alinhamento === "centro"
                                    ? "justify-center"
                                    : "justify-start") +
                                (ativa ? " text-slate-800" : "")
                              }
                            >
                              {coluna.titulo}
                              {/*
                                O ícone ocupa o mesmo espaço nos três estados,
                                então ordenar não faz o cabeçalho pular de
                                largura. Inativo, ele fica pálido — convite, não
                                informação.
                              */}
                              <IconeDeOrdenacao
                                estado={
                                  !ativa
                                    ? "neutro"
                                    : ordem.direcao === "Crescente"
                                      ? "crescente"
                                      : "decrescente"
                                }
                                className={ativa ? "size-3.5 text-marca-600" : "size-3.5 text-slate-300"}
                              />
                            </button>
                          ) : (
                            <span className="block px-4 py-3">{coluna.titulo}</span>
                          )}
                        </th>
                      );
                    })}

                    <th scope="col" className="w-px px-4 py-3">
                      <span className="sr-only">Ações</span>
                    </th>
                  </tr>
                </thead>

                <tbody>
                  {itens.map((pessoa) => {
                    const marcada = selecao.marcadas.has(pessoa.id);

                    return (
                      <tr
                        key={pessoa.id}
                        onClick={(evento) => {
                          if (cliqueVeioDeControle(evento)) return;
                          selecao.alternar(pessoa.id);
                        }}
                        onDoubleClick={() => abrir(pessoa.id)}
                        className={
                          "group cursor-pointer border-b border-borda transition-colors last:border-0 " +
                          (marcada ? "bg-marca-50" : "hover:bg-slate-50")
                        }
                      >
                        {/*
                          A coluna da marcação existe sempre, com a mesma
                          largura. Fazer a caixa aparecer só depois da primeira
                          marcação empurraria a tabela inteira para o lado no
                          exato quadro em que a pessoa acabou de clicar.
                        */}
                        <td className="pr-2 pl-4">
                          <input
                            type="checkbox"
                            checked={marcada}
                            onChange={() => selecao.alternar(pessoa.id)}
                            aria-label={`Selecionar ${pessoa.nome}`}
                            className={
                              "size-4 rounded border-borda-forte accent-marca-600 transition-opacity " +
                              (algumaMarcada
                                ? "opacity-100"
                                : "opacity-0 group-hover:opacity-60 focus:opacity-100")
                            }
                          />
                        </td>

                        {colunas.map((coluna, indice) => (
                          <td
                            key={coluna.chave}
                            className={`px-4 py-4 ${classeDeAlinhamento(coluna)} ${coluna.classe ?? ""}`}
                          >
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

                        <td className="px-2 py-2">
                          <MenuDeAcoes rotulo={pessoa.nome} acoes={acoesDaLinha(pessoa)} />
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>

            <Paginacao
              pagina={pessoas.data.pagina}
              tamanho={pessoas.data.tamanho}
              total={pessoas.data.total}
              aoMudar={definirPagina}
              aoMudarTamanho={(novo) => {
                definirTamanho(novo);
                /* A página sete de 25 em 25 não existe de 100 em 100. */
                definirPagina(1);
              }}
              comNumeros
              substantivo={{ singular: "cadastro", plural: "cadastros" }}
            />

            <BarraDeSelecao
              quantidade={selecao.quantidade}
              ocupada={inativando}
              aoEditar={() => abrir(selecao.ids[0])}
              aoInativar={inativarSelecionadas}
              aoLimpar={selecao.limpar}
            />
          </>
        )}
      </div>

      <VoltarAoTopo />
    </>
  );
}

/**
 * O lugar da tabela enquanto ela não chegou.
 *
 * Com a mesma quantidade de colunas do que vai aparecer, para o conteúdo real
 * ocupar o espaço já reservado em vez de empurrar a página para baixo.
 */
function Esqueleto({ colunas }: { colunas: number }) {
  return (
    <div
      role="status"
      aria-label="Carregando o cadastro"
      className="overflow-hidden rounded-[--radius-cartao] border border-borda bg-superficie shadow-nivel-1"
    >
      {Array.from({ length: 8 }).map((_, linha) => (
        <div
          key={linha}
          className="flex items-center gap-4 border-b border-borda px-4 py-4 last:border-0"
        >
          {Array.from({ length: colunas }).map((__, coluna) => (
            <div
              key={coluna}
              className={
                "h-3 rounded bg-slate-100 motion-safe:animate-pulse " +
                (coluna === 0 ? "w-12" : coluna === 1 ? "flex-1" : "w-28")
              }
            />
          ))}
        </div>
      ))}
    </div>
  );
}
