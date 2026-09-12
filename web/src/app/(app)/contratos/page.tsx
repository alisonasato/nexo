"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { Botao, Entrada, Selecao } from "@/componentes/controles";
import { Paginacao } from "@/componentes/paginacao";
import { VoltarAoTopo } from "@/componentes/voltar-ao-topo";
import { competenciaAtual, formatarValor } from "@/lib/dinheiro";
import { useConsultaDaUrl, useLinkComRetorno } from "@/lib/estado-na-url";
import { CabecalhoOrdenavel, useOrdenacao } from "@/componentes/tabela";

type SituacaoContrato = components["schemas"]["SituacaoContrato"];
type ContratoNaLista = components["schemas"]["ContratoNaLista"];
type OrdemDeContratos = components["schemas"]["OrdemDeContratos"];

const situacoes: Record<SituacaoContrato, string> = {
  Ativo: "Ativo",
  Suspenso: "Suspenso",
  Encerrado: "Encerrado",
};

/** A tarja da situação, igual no cartão e na tabela. */
function Situacao({ situacao }: { situacao: ContratoNaLista["situacao"] }) {
  return (
    <span
      className={
        "rounded-full px-2 py-0.5 text-xs font-semibold " +
        (situacao === "Ativo"
          ? "bg-emerald-50 text-emerald-800"
          : situacao === "Suspenso"
            ? "bg-amber-50 text-amber-800"
            : "bg-slate-100 text-slate-600")
      }
    >
      {situacoes[situacao] ?? situacao}
    </span>
  );
}

export default function ListagemDeContratos() {
  const clienteDeConsultas = useQueryClient();
  const hoje = competenciaAtual();

  /*
   * Busca, situação, página e ordem moram na URL — o mesmo desenho da listagem
   * de pessoas, e pelo mesmo motivo: a lista vira link, e voltar de um contrato
   * devolve o recorte em vez da página 1 sem filtro.
   *
   * A competência da geração <b>não</b> mora lá. Ela não recorta a lista: é o
   * parâmetro de uma ação, e um link que já viesse com mês e ano preenchidos
   * seria um convite a gerar a competência de outra pessoa.
   */
  const { ler, gravar, estabilizada } = useConsultaDaUrl("/contratos");
  const comRecorte = useLinkComRetorno();

  const [ano, definirAno] = useState(hoje.ano);
  const [mes, definirMes] = useState(hoje.mes);

  const busca = ler("busca") ?? "";
  const situacao = (ler("situacao") ?? "") as SituacaoContrato | "";
  const pagina = Math.max(1, Number(ler("pagina")) || 1);

  const { ordenarPor, ...ordem } = useOrdenacao<OrdemDeContratos>(
    { ler, gravar },
    "Codigo",
  );

  /* Nulo enquanto ninguém digitou: o campo mostra o que a URL disser. */
  const [rascunho, definirRascunho] = useState<string | null>(null);
  const textoDaBusca = rascunho ?? busca;

  /*
   * Quem foi escolhido para gerar. Guarda ids, e não linhas, para a escolha
   * atravessar página, busca e filtro — quem marca três contratos, procura o
   * quarto e marca também espera encontrar os quatro no fim.
   */
  const [escolhidos, definirEscolhidos] = useState<Set<string>>(new Set());

  /* Mesmo meio segundo de espera da tela de pessoas: uma ida ao servidor por
     pausa, não por tecla. Quem espera é a URL, e a consulta sai dela pronta. */
  useEffect(() => {
    if (rascunho === null || rascunho === busca) return;

    /* Busca nova recomeça na primeira página: a sétima do termo velho não
       existe mais. */
    const relogio = setTimeout(() => gravar({ busca: rascunho, pagina: null }), 500);
    return () => clearTimeout(relogio);
  }, [rascunho, busca, gravar]);

  const contratos = useQuery({
    queryKey: ["contratos", busca, situacao, pagina, ordem],
    queryFn: async () => {
      const { data, error } = await api.GET("/contratos", {
        params: {
          query: {
            pagina,
            ordenarPor: ordem.por,
            direcao: ordem.direcao,
            ...(busca ? { busca } : {}),
            ...(situacao ? { situacao } : {}),
          },
        },
      });
      if (error || !data) throw new Error("Não foi possível carregar os contratos.");
      return data;
    },
    /* Chegando por navegação, o primeiro render enxerga a URL da tela anterior:
       consultar ali sairia com o recorte errado. */
    enabled: estabilizada,
    placeholderData: keepPreviousData,
  });

  const gerar = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/contratos/gerar-mensalidades", {
        /*
         * Nenhum escolhido quer dizer "todos", e é `null` que diz isso. Mandar
         * lista vazia significa "nenhum" para a API, que é coisa diferente — e a
         * diferença entre gerar a competência inteira e não gerar nada não pode
         * depender de um detalhe de serialização.
         */
        body: { ano, mes, contratoIds: escolhidos.size > 0 ? [...escolhidos] : null },
      });
      if (error || !data) throw new Error("Não foi possível gerar as mensalidades.");
      return data;
    },
    onSuccess: () => {
      definirEscolhidos(new Set());
      clienteDeConsultas.invalidateQueries({ queryKey: ["recebiveis"] });
    },
  });

  /* Busca e situação afetam a lista da mesma forma: escondem parte do todo. */
  const filtrando = Boolean(busca || situacao);

  const definirPagina = (numero: number) => gravar({ pagina: numero === 1 ? null : numero });

  const naPagina = contratos.data?.itens ?? [];
  const todosDaPaginaEscolhidos =
    naPagina.length > 0 && naPagina.every((contrato) => escolhidos.has(contrato.id));

  function alternar(id: string) {
    definirEscolhidos((atual) => {
      const proximo = new Set(atual);
      if (!proximo.delete(id)) proximo.add(id);
      return proximo;
    });
  }

  /*
   * A caixa do cabeçalho age só sobre a página, e não sobre o conjunto todo.
   * Marcar o que não está à vista é o tipo de atalho que gera cobrança para
   * quem ninguém pretendia cobrar.
   */
  function alternarAPagina() {
    definirEscolhidos((atual) => {
      const proximo = new Set(atual);
      for (const contrato of naPagina) {
        if (todosDaPaginaEscolhidos) proximo.delete(contrato.id);
        else proximo.add(contrato.id);
      }
      return proximo;
    });
  }

  return (
    <>
      <header className="flex flex-wrap items-center justify-between gap-4 border-b border-borda bg-superficie px-6 py-4">
        <div>
          <h1 id="conteudo" tabIndex={-1} className="text-xl font-semibold tracking-tight text-marca-950">
            Contratos
          </h1>
          <p className="text-slate-600">O que cada cliente paga por mês, e desde quando.</p>
        </div>

        <Link
          href={comRecorte("/contratos/novo")}
          className="inline-flex items-center rounded-[--radius-controle] bg-marca-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-marca-700"
        >
          Novo contrato
        </Link>
      </header>

      <div className="flex flex-1 flex-col gap-6 p-6">
        <section className="flex flex-col gap-3 rounded-[--radius-cartao] border border-borda bg-superficie p-5">
          <div>
            <h2 className="font-semibold text-marca-950">Gerar mensalidades</h2>
            <p className="text-slate-600">
              Cria um recebível para cada contrato ativo na competência escolhida. Marque linhas
              na lista abaixo para gerar só as escolhidas. Pode ser executado quantas vezes for
              preciso: o que já existe é ignorado, nunca duplicado.
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

            {/*
              O rótulo diz o alcance, e não só a ação. "Gerar" sozinho esconde a
              diferença entre atingir três contratos e atingir a carteira
              inteira — e é uma diferença que só se desfaz cancelando recebível
              por recebível.
            */}
            <Botao type="button" disabled={gerar.isPending} onClick={() => gerar.mutate()}>
              {gerar.isPending
                ? "Gerando…"
                : escolhidos.size > 0
                  ? `Gerar para ${escolhidos.size} escolhido${escolhidos.size > 1 ? "s" : ""}`
                  : "Gerar para todos"}
            </Botao>

            {escolhidos.size > 0 && (
              <button
                type="button"
                onClick={() => definirEscolhidos(new Set())}
                className="py-2 text-sm font-medium text-marca-700 hover:underline"
              >
                Limpar seleção
              </button>
            )}
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

        <div className="flex items-center gap-3">
          <input
            type="search"
            value={textoDaBusca}
            onChange={(evento) => definirRascunho(evento.target.value)}
            placeholder="Buscar por código, descrição ou cliente"
            aria-label="Buscar contratos"
            className="w-full max-w-md rounded-[--radius-controle] border border-borda-forte bg-superficie px-3 py-2 placeholder:text-slate-400 focus:border-marca-500"
          />
          {contratos.isFetching && <span className="text-xs text-slate-600">buscando…</span>}
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {([["", "Todas"], ...Object.entries(situacoes)] as const).map(([valor, rotulo]) => (
            <button
              key={rotulo}
              type="button"
              /* A página sete de uma situação não é a página sete de outra. */
              onClick={() => gravar({ situacao: valor, pagina: null })}
              aria-pressed={situacao === valor}
              className={
                "inline-flex min-h-9 items-center rounded-full px-3.5 text-sm font-medium transition-colors " +
                (situacao === valor
                  ? "bg-marca-600 text-white"
                  : "border border-borda-forte bg-superficie text-slate-700 hover:bg-slate-50")
              }
            >
              {rotulo}
            </button>
          ))}
        </div>

        {contratos.isPending && <p className="text-slate-600">Carregando os contratos…</p>}

        {contratos.isError && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            {contratos.error.message}
          </p>
        )}

        {contratos.data && contratos.data.itens.length === 0 && (
          <div className="rounded-[--radius-cartao] border border-dashed border-borda-forte bg-superficie px-6 py-12 text-center">
            <p className="font-medium text-slate-700">
              {filtrando ? "Nenhum contrato encontrado." : "Nenhum contrato cadastrado."}
            </p>
            <p className="mt-1 text-slate-600">
              {busca && situacao
                ? "Nenhum contrato nessa situação bate com a busca. Tente outra situação."
                : busca
                  ? "Tente o código, um pedaço da descrição ou o nome do cliente."
                  : situacao
                    ? "Nenhum contrato nessa situação."
                    : "Um contrato precisa de alguém marcado como cliente. Abra Pessoas, cadastre, e marque o papel Cliente."}
            </p>
          </div>
        )}

        {contratos.data && contratos.data.itens.length > 0 && (
          <>
            {/*
              No celular, cartão; da largura média para cima, tabela.

              A tabela tem sete colunas e não cabe em 375 pixels: rolar de lado
              esconde justamente o valor e a situação, que são o que se vai
              olhar. O cartão mostra tudo de uma vez, e a caixa de escolher
              continua sendo a mesma coisa — é por ela que se decide o que
              entra na geração de mensalidades.
            */}
            <ul className="flex flex-col gap-3 md:hidden">
              {contratos.data.itens.map((contrato) => (
                <li key={contrato.id}>
                  <article
                    className={
                      "rounded-[--radius-cartao] border p-4 transition-all " +
                      (escolhidos.has(contrato.id)
                        ? "border-marca-400 bg-marca-50 shadow-nivel-2"
                        : "border-borda bg-superficie shadow-nivel-1")
                    }
                  >
                    <div className="flex items-start gap-3">
                      <input
                        type="checkbox"
                        checked={escolhidos.has(contrato.id)}
                        onChange={() => alternar(contrato.id)}
                        aria-label={`Escolher o contrato ${contrato.codigo}`}
                        className="mt-1 size-5 shrink-0 rounded border-borda-forte accent-marca-600"
                      />

                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-center gap-2">
                          <Link
                            href={comRecorte(`/contratos/${contrato.id}`)}
                            className="numeros-tabulares font-semibold text-marca-700 hover:underline"
                          >
                            {contrato.codigo}
                          </Link>
                          <Situacao situacao={contrato.situacao} />
                        </div>

                        <p className="mt-1 font-medium text-slate-800">{contrato.nomeDaPessoa}</p>

                        <dl className="mt-2 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
                          <dt className="text-xs tracking-wide text-slate-400 uppercase">Descrição</dt>
                          <dd className="min-w-0 text-slate-700">{contrato.descricao}</dd>

                          <dt className="text-xs tracking-wide text-slate-400 uppercase">Valor</dt>
                          <dd className="numeros-tabulares font-medium text-slate-800">
                            {formatarValor(contrato.valor)}
                          </dd>

                          <dt className="text-xs tracking-wide text-slate-400 uppercase">Vence</dt>
                          {/* No cartão o rótulo já diz "vence", então o dia sozinho
                              ficaria solto: aqui ele ganha a palavra de volta. */}
                          <dd className="numeros-tabulares text-slate-700">
                            dia {contrato.diaDeVencimento}
                          </dd>
                        </dl>
                      </div>
                    </div>
                  </article>
                </li>
              ))}
            </ul>

            <div className="hidden overflow-x-auto rounded-[--radius-cartao] border border-borda bg-superficie shadow-nivel-1 md:block">
              <table className="w-full min-w-3xl border-collapse text-left">
                <thead>
                  <tr className="border-b border-borda bg-slate-50/60 text-xs tracking-wide text-slate-500 uppercase">
                    <th scope="col" className="w-10 px-4 py-3">
                      <input
                        type="checkbox"
                        checked={todosDaPaginaEscolhidos}
                        onChange={alternarAPagina}
                        aria-label="Escolher os contratos desta página"
                        className="size-4 rounded border-borda-forte accent-marca-600"
                      />
                    </th>

                    <CabecalhoOrdenavel
                      titulo="Código"
                      por="Codigo"
                      ordem={ordem}
                      aoOrdenar={ordenarPor}
                    />
                    <CabecalhoOrdenavel
                      titulo="Cliente"
                      por="Cliente"
                      ordem={ordem}
                      aoOrdenar={ordenarPor}
                    />
                    {/* Descrição não ordena: a API não sabe ordenar por ela, e um
                        cabeçalho que responde ao clique sem mudar nada é pior
                        que um cabeçalho quieto. */}
                    <CabecalhoOrdenavel titulo="Descrição" ordem={ordem} aoOrdenar={ordenarPor} />
                    <CabecalhoOrdenavel
                      titulo="Valor"
                      alinhamento="direita"
                      por="Valor"
                      ordem={ordem}
                      aoOrdenar={ordenarPor}
                    />
                    <CabecalhoOrdenavel
                      titulo="Vencimento"
                      alinhamento="direita"
                      por="Vencimento"
                      ordem={ordem}
                      aoOrdenar={ordenarPor}
                    />
                    <CabecalhoOrdenavel titulo="Situação" ordem={ordem} aoOrdenar={ordenarPor} />
                  </tr>
                </thead>
                <tbody>
                  {contratos.data.itens.map((contrato) => (
                    <tr
                      key={contrato.id}
                      className={
                        "border-b border-borda last:border-0 " +
                        (escolhidos.has(contrato.id) ? "bg-marca-50" : "hover:bg-marca-50")
                      }
                    >
                      <td className="px-4 py-3">
                        <input
                          type="checkbox"
                          checked={escolhidos.has(contrato.id)}
                          onChange={() => alternar(contrato.id)}
                          aria-label={`Escolher o contrato ${contrato.codigo}`}
                          className="size-4 rounded border-borda-forte accent-marca-600"
                        />
                      </td>
                      <td className="numeros-tabulares px-4 py-3">
                        <Link
                          href={comRecorte(`/contratos/${contrato.id}`)}
                          className="font-medium text-marca-700 hover:underline"
                        >
                          {contrato.codigo}
                        </Link>
                      </td>
                      <td className="px-4 py-3 text-slate-700">
                        <span className="numeros-tabulares text-slate-600">
                          {contrato.codigoDaPessoa}
                        </span>{" "}
                        {contrato.nomeDaPessoa}
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
                        <Situacao situacao={contrato.situacao} />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="flex flex-col gap-2">
              <Paginacao
                pagina={contratos.data.pagina}
                tamanho={contratos.data.tamanho}
                total={contratos.data.total}
                aoMudar={definirPagina}
                substantivo={{ singular: "contrato", plural: "contratos" }}
              />

              {/* Somado no servidor sobre todos os ativos — não sobre esta página. */}
              <p className="text-slate-600">
                <span className="numeros-tabulares font-medium text-slate-700">
                  {formatarValor(contratos.data.totalMensalAtivo)}
                </span>{" "}
                por mês em contratos ativos
                {/* Dizer isto em voz alta enquanto há busca: o número não é a
                    soma do que está na tela, e parecer que é seria pior do que
                    não mostrar nada. */}
                {filtrando && ", todos: não só os que a lista mostra"}
              </p>
            </div>
          </>
        )}
      </div>

      <VoltarAoTopo />
    </>
  );
}
