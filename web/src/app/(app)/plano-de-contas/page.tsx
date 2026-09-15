"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { useAvisos } from "@/componentes/avisos";
import { Botao } from "@/componentes/controles";

import { GavetaDeCategoria, type EdicaoDeCategoria } from "./gaveta-de-categoria";
import { GavetaDeCentro } from "./gaveta-de-centro";

type CategoriaNaLista = components["schemas"]["CategoriaNaLista"];
type CentroNaLista = components["schemas"]["CentroNaLista"];
type NaturezaLancamento = components["schemas"]["NaturezaLancamento"];

const compacto = "min-h-11 px-4 text-xs md:min-h-0 md:px-3 md:py-1";

const MAXIMO_DE_NIVEIS = 3;

const lados: [NaturezaLancamento, string][] = [
  ["Receber", "A receber"],
  ["Pagar", "A pagar"],
];

/**
 * O plano de contas: o que o dinheiro foi, e de quem ele é.
 *
 * <p>
 * <b>Uma coluna por natureza.</b> A árvore de receitas e a de despesas nunca se
 * misturam, e lado a lado fica à vista que a categoria certa para um gasto
 * está do lado de a pagar, e não perdida numa lista única.
 * </p>
 * <p>
 * <b>Recuo, e não árvore que abre e fecha.</b> Um plano de escritório pequeno
 * cabe inteiro na tela. Esconder galhos atrás de setas obrigaria a abrir cada um
 * para achar onde a categoria nova devia entrar.
 * </p>
 */
export default function PlanoDeContas() {
  const clienteDeConsultas = useQueryClient();
  const avisar = useAvisos();

  const [categoriaEmEdicao, definirCategoriaEmEdicao] = useState<EdicaoDeCategoria | null>(null);
  const [centroEmEdicao, definirCentroEmEdicao] = useState<CentroNaLista | "novo" | null>(null);

  const categorias = useQuery({
    queryKey: ["categorias"],
    queryFn: async () => {
      const { data, error } = await api.GET("/categorias");
      if (error || !data) throw new Error("Não foi possível carregar as categorias.");
      return data;
    },
  });

  const centros = useQuery({
    queryKey: ["centros-de-custo"],
    queryFn: async () => {
      const { data, error } = await api.GET("/centros-de-custo");
      if (error || !data) throw new Error("Não foi possível carregar os centros de custo.");
      return data;
    },
  });

  const planoSugerido = useMutation({
    mutationFn: async () => {
      const { error } = await api.POST("/categorias/plano-sugerido");
      if (error) throw error;
    },
    onSuccess: () => {
      clienteDeConsultas.invalidateQueries({ queryKey: ["categorias"] });
      avisar({ tom: "sucesso", titulo: "Plano sugerido criado.", detalhe: "Renomeie, inative e complete à vontade." });
    },
    onError: () => avisar({ tom: "erro", titulo: "O plano sugerido não foi criado." }),
  });

  const listaDeCategorias = categorias.data ?? [];
  const listaDeCentros = centros.data ?? [];

  function linhaDaCategoria(categoria: CategoriaNaLista) {
    return (
      <li
        key={categoria.id}
        className="flex flex-wrap items-center justify-between gap-2 border-b border-borda py-2 last:border-0"
        /* O recuo mostra o nível: 1rem por degrau, a partir da raiz. */
        style={{ paddingLeft: `${(categoria.nivel - 1) * 1.25}rem` }}
      >
        <span className={"min-w-0 " + (categoria.nivel === 1 ? "font-semibold text-slate-800" : "text-slate-700")}>
          {categoria.nome}
          {!categoria.ativa && (
            <span className="ml-2 rounded-full bg-slate-200 px-2 py-0.5 text-xs font-semibold text-slate-600">
              Inativa
            </span>
          )}
        </span>

        <span className="flex gap-1">
          {categoria.ativa && categoria.nivel < MAXIMO_DE_NIVEIS && (
            <Botao
              aparencia="secundario"
              type="button"
              className={compacto}
              onClick={() => definirCategoriaEmEdicao({ tipo: "nova", natureza: categoria.natureza, paiId: categoria.id })}
              aria-label={`Criar categoria debaixo de ${categoria.caminho}`}
            >
              + Debaixo
            </Botao>
          )}
          <Botao
            aparencia="secundario"
            type="button"
            className={compacto}
            onClick={() => definirCategoriaEmEdicao({ tipo: "alterar", categoria })}
            aria-label={`Alterar ${categoria.caminho}`}
          >
            Alterar
          </Botao>
        </span>
      </li>
    );
  }

  return (
    <>
      <header className="border-b border-borda bg-superficie px-6 py-4">
        <h1 id="conteudo" tabIndex={-1} className="text-xl font-semibold tracking-tight text-marca-950">
          Plano de contas
        </h1>
        <p className="text-slate-600">
          As categorias dizem o que o dinheiro foi; os centros de custo, de quem ele é dentro do escritório.
        </p>
      </header>

      <div className="flex flex-1 flex-col gap-8 p-6">
        <section aria-labelledby="titulo-das-categorias" className="flex flex-col gap-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h2 id="titulo-das-categorias" className="text-lg font-semibold text-marca-950">
                Categorias
              </h2>
              <p className="text-slate-600">Até três níveis. A natureza de cada ramo vem da categoria do topo.</p>
            </div>
            <Botao type="button" onClick={() => definirCategoriaEmEdicao({ tipo: "nova", natureza: "Pagar", paiId: null })}>
              Nova categoria
            </Botao>
          </div>

          {categorias.isPending && <p className="text-slate-600">Carregando as categorias…</p>}

          {categorias.isError && (
            <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
              {categorias.error.message}
            </p>
          )}

          {categorias.data && listaDeCategorias.length === 0 && (
            <div className="rounded-[--radius-cartao] border border-dashed border-borda-forte bg-superficie px-6 py-10 text-center">
              <p className="font-medium text-slate-700">Nenhuma categoria ainda.</p>
              <p className="mx-auto mt-1 max-w-xl text-slate-600">
                Dá para criar uma a uma, ou começar pelo plano sugerido para escritório de contabilidade:
                honorários e outras receitas de um lado; pessoal, ocupação, operação, impostos e financeiro do
                outro. Tudo nele se renomeia e se inativa depois.
              </p>
              <Botao
                type="button"
                className="mt-4"
                disabled={planoSugerido.isPending}
                onClick={() => planoSugerido.mutate()}
              >
                {planoSugerido.isPending ? "Criando…" : "Criar o plano sugerido"}
              </Botao>
            </div>
          )}

          {listaDeCategorias.length > 0 && (
            <div className="grid gap-4 lg:grid-cols-2">
              {lados.map(([natureza, rotulo]) => {
                const doLado = listaDeCategorias.filter((categoria) => categoria.natureza === natureza);

                return (
                  <div
                    key={natureza}
                    className="rounded-[--radius-cartao] border border-borda bg-superficie p-4 shadow-nivel-1"
                  >
                    <h3 className="mb-2 text-xs font-semibold tracking-wide text-slate-500 uppercase">{rotulo}</h3>
                    {doLado.length === 0 ? (
                      <p className="py-2 text-slate-500">Nenhuma categoria {rotulo.toLowerCase()}.</p>
                    ) : (
                      <ul>{doLado.map(linhaDaCategoria)}</ul>
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </section>

        <section aria-labelledby="titulo-dos-centros" className="flex flex-col gap-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h2 id="titulo-dos-centros" className="text-lg font-semibold text-marca-950">
                Centros de custo
              </h2>
              <p className="text-slate-600">Unidades, áreas ou projetos. Uma lista só, para as duas naturezas.</p>
            </div>
            <Botao type="button" onClick={() => definirCentroEmEdicao("novo")}>
              Novo centro de custo
            </Botao>
          </div>

          {centros.isPending && <p className="text-slate-600">Carregando os centros de custo…</p>}

          {centros.isError && (
            <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
              {centros.error.message}
            </p>
          )}

          {centros.data && listaDeCentros.length === 0 && (
            <p className="rounded-[--radius-cartao] border border-dashed border-borda-forte bg-superficie px-6 py-6 text-center text-slate-600">
              Nenhum centro de custo. Escritório de uma unidade só costuma não precisar deles.
            </p>
          )}

          {listaDeCentros.length > 0 && (
            <ul className="rounded-[--radius-cartao] border border-borda bg-superficie px-4 shadow-nivel-1">
              {listaDeCentros.map((centro) => (
                <li
                  key={centro.id}
                  className="flex flex-wrap items-center justify-between gap-2 border-b border-borda py-2 last:border-0"
                >
                  <span className="text-slate-800">
                    {centro.nome}
                    {!centro.ativo && (
                      <span className="ml-2 rounded-full bg-slate-200 px-2 py-0.5 text-xs font-semibold text-slate-600">
                        Inativo
                      </span>
                    )}
                  </span>
                  <Botao
                    aparencia="secundario"
                    type="button"
                    className={compacto}
                    onClick={() => definirCentroEmEdicao(centro)}
                    aria-label={`Alterar ${centro.nome}`}
                  >
                    Alterar
                  </Botao>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>

      <GavetaDeCategoria
        key={
          categoriaEmEdicao === null
            ? "fechada"
            : categoriaEmEdicao.tipo === "alterar"
              ? categoriaEmEdicao.categoria.id
              : `nova-${categoriaEmEdicao.paiId ?? categoriaEmEdicao.natureza}`
        }
        edicao={categoriaEmEdicao}
        categorias={listaDeCategorias}
        aoFechar={() => definirCategoriaEmEdicao(null)}
      />

      <GavetaDeCentro
        key={centroEmEdicao === null ? "fechada" : centroEmEdicao === "novo" ? "novo" : centroEmEdicao.id}
        centro={centroEmEdicao}
        aoFechar={() => definirCentroEmEdicao(null)}
      />
    </>
  );
}
