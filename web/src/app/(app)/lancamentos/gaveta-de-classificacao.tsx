"use client";

import Link from "next/link";
import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { useAvisos } from "@/componentes/avisos";
import { Botao, Selecao } from "@/componentes/controles";
import { Gaveta } from "@/componentes/gaveta";

import { useCategorias, useCentrosDeCusto } from "./classificacao";

type LancamentoNaLista = components["schemas"]["LancamentoNaLista"];
type Problema = components["schemas"]["Problema"];

function problemasDaResposta(erro: unknown): Problema[] {
  if (erro && typeof erro === "object" && "problemas" in erro && Array.isArray(erro.problemas)) {
    return erro.problemas as Problema[];
  }
  return [];
}

/**
 * Nula quando fechada. Quem abre passa uma chave com o id do lançamento, para
 * cada abertura começar da classificação gravada, e não da escolhida antes.
 */
type Props = { lancamento: LancamentoNaLista | null; aoFechar: () => void };

/**
 * Classificar um lançamento que já existe, em qualquer situação.
 *
 * <p>
 * As listas só oferecem o que a API aceitaria: categorias da natureza do
 * lançamento e centros ativos. A exceção é o que já está gravado nele, que
 * continua na lista mesmo inativo, para trocar o centro sem perder a categoria.
 * </p>
 */
export function GavetaDeClassificacao({ lancamento, aoFechar }: Props) {
  const avisar = useAvisos();
  const clienteDeConsultas = useQueryClient();

  const [categoria, definirCategoria] = useState(lancamento?.categoriaId ?? "");
  const [centro, definirCentro] = useState(lancamento?.centroDeCustoId ?? "");
  const [problemas, definirProblemas] = useState<Problema[]>([]);

  const todasAsCategorias = useCategorias().data;
  const todosOsCentros = useCentrosDeCusto().data;

  const categorias = (todasAsCategorias ?? []).filter(
    (item) => item.natureza === lancamento?.natureza && (item.ativa || item.id === lancamento?.categoriaId),
  );
  const centros = (todosOsCentros ?? []).filter((item) => item.ativo || item.id === lancamento?.centroDeCustoId);

  const classificar = useMutation({
    mutationFn: async () => {
      const { error } = await api.PUT("/lancamentos/{id}/classificacao", {
        params: { path: { id: lancamento!.id } },
        body: { categoriaId: categoria || null, centroDeCustoId: centro || null },
      });
      if (error) throw error;
    },
    onSuccess: () => {
      clienteDeConsultas.invalidateQueries({ queryKey: ["lancamentos"] });
      avisar({ tom: "sucesso", titulo: "Lançamento classificado." });
      aoFechar();
    },
    onError: (erro: unknown) => definirProblemas(problemasDaResposta(erro)),
  });

  const falhaSemMotivo = classificar.isError && problemas.length === 0;
  const semPlano = todasAsCategorias !== undefined && todasAsCategorias.length === 0;

  return (
    <Gaveta
      titulo="Classificar"
      descricao={lancamento ? `${lancamento.nomeDaPessoa} · ${lancamento.descricao}` : undefined}
      aberta={lancamento !== null}
      aoFechar={aoFechar}
      rodape={
        <div className="flex justify-end gap-2">
          <Botao aparencia="secundario" type="button" onClick={aoFechar}>
            Cancelar
          </Botao>
          <Botao type="submit" form="formulario-de-classificacao" disabled={classificar.isPending}>
            {classificar.isPending ? "Salvando…" : "Salvar"}
          </Botao>
        </div>
      }
    >
      <form
        id="formulario-de-classificacao"
        className="flex flex-col gap-4"
        onSubmit={(evento) => {
          evento.preventDefault();
          classificar.mutate();
        }}
      >
        {(problemas.length > 0 || falhaSemMotivo) && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            {problemas.length > 0
              ? `${problemas[0].descricao} ${problemas[0].sugestao}`
              : "Não foi possível classificar. Tente de novo em instantes."}
          </p>
        )}

        {semPlano && (
          <p className="rounded-[--radius-controle] bg-amber-50 px-4 py-3 text-amber-900">
            O plano de contas ainda está vazio.{" "}
            <Link href="/plano-de-contas" className="font-semibold underline underline-offset-2">
              Crie as categorias
            </Link>{" "}
            para classificar.
          </p>
        )}

        <Selecao rotulo="Categoria" value={categoria} onChange={(evento) => definirCategoria(evento.target.value)}>
          <option value="">Sem categoria</option>
          {categorias.map((item) => (
            <option key={item.id} value={item.id}>
              {item.caminho}
              {item.ativa ? "" : " (inativa)"}
            </option>
          ))}
        </Selecao>

        <Selecao rotulo="Centro de custo" value={centro} onChange={(evento) => definirCentro(evento.target.value)}>
          <option value="">Sem centro de custo</option>
          {centros.map((item) => (
            <option key={item.id} value={item.id}>
              {item.nome}
              {item.ativo ? "" : " (inativo)"}
            </option>
          ))}
        </Selecao>

        <p className="text-sm text-slate-600">
          Classificar não mexe no dinheiro: vale para lançamento em aberto, pago ou cancelado.
        </p>
      </form>
    </Gaveta>
  );
}
