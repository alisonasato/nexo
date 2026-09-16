"use client";

import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { problemasDaResposta } from "@/lib/problemas";
import { useAvisos } from "@/componentes/avisos";
import { Botao, Entrada, Selecao } from "@/componentes/controles";
import { Gaveta } from "@/componentes/gaveta";

type CategoriaNaLista = components["schemas"]["CategoriaNaLista"];
type NaturezaLancamento = components["schemas"]["NaturezaLancamento"];
type Problema = components["schemas"]["Problema"];

const MAXIMO_DE_NIVEIS = 3;

/** Criar, já com a natureza e o lugar sugeridos por onde se clicou; ou alterar uma que existe. */
export type EdicaoDeCategoria =
  | { tipo: "nova"; natureza: NaturezaLancamento; paiId: string | null }
  | { tipo: "alterar"; categoria: CategoriaNaLista };

/** A categoria e tudo o que está abaixo dela: nenhum desses serve de lugar para ela mesma. */
function galho(raiz: string, categorias: CategoriaNaLista[]): Set<string> {
  const ids = new Set([raiz]);
  let cresceu = true;

  while (cresceu) {
    cresceu = false;
    for (const categoria of categorias) {
      if (categoria.paiId && ids.has(categoria.paiId) && !ids.has(categoria.id)) {
        ids.add(categoria.id);
        cresceu = true;
      }
    }
  }

  return ids;
}

/** Quantos níveis o galho ocupa, contando a própria categoria. */
function altura(raiz: string, categorias: CategoriaNaLista[]): number {
  const filhas = categorias.filter((categoria) => categoria.paiId === raiz);
  return filhas.length === 0 ? 1 : 1 + Math.max(...filhas.map((filha) => altura(filha.id, categorias)));
}

type Props = { edicao: EdicaoDeCategoria | null; categorias: CategoriaNaLista[]; aoFechar: () => void };

/**
 * Criar, renomear, mover ou inativar uma categoria.
 *
 * <p>
 * <b>A lista de "fica debaixo de" só oferece o que a API aceitaria:</b> a mesma
 * natureza, fora do próprio galho, e só onde o galho inteiro ainda cabe nos três
 * níveis. Descobrir isso só ao salvar seria escolher de novo sem saber por quê.
 * </p>
 * <p>
 * A natureza só se escolhe ao criar na raiz. Debaixo de outra, ela é a de cima;
 * depois de criada, não muda.
 * </p>
 */
export function GavetaDeCategoria({ edicao, categorias, aoFechar }: Props) {
  const avisar = useAvisos();
  const clienteDeConsultas = useQueryClient();
  const existente = edicao?.tipo === "alterar" ? edicao.categoria : null;

  const [nome, definirNome] = useState(existente?.nome ?? "");
  const [natureza, definirNatureza] = useState<NaturezaLancamento>(
    existente?.natureza ?? (edicao?.tipo === "nova" ? edicao.natureza : "Pagar"),
  );
  const [paiId, definirPaiId] = useState(
    existente?.paiId ?? (edicao?.tipo === "nova" ? (edicao.paiId ?? "") : ""),
  );
  const [ativa, definirAtiva] = useState(existente?.ativa ?? true);
  const [problemas, definirProblemas] = useState<Problema[]>([]);

  const proibidos = existente ? galho(existente.id, categorias) : new Set<string>();
  const alturaDoGalho = existente ? altura(existente.id, categorias) : 1;

  const lugares = categorias.filter(
    (categoria) =>
      categoria.natureza === natureza &&
      !proibidos.has(categoria.id) &&
      categoria.nivel + alturaDoGalho <= MAXIMO_DE_NIVEIS &&
      (categoria.ativa || categoria.id === paiId),
  );

  const pai = categorias.find((categoria) => categoria.id === paiId);

  const salvar = useMutation({
    mutationFn: async () => {
      const body = { nome, natureza, paiId: paiId || null, ativa };

      const { error } = existente
        ? await api.PUT("/categorias/{id}", { params: { path: { id: existente.id } }, body })
        : await api.POST("/categorias", { body });

      if (error) throw error;
    },
    onSuccess: () => {
      clienteDeConsultas.invalidateQueries({ queryKey: ["categorias"] });
      avisar({ tom: "sucesso", titulo: existente ? "Categoria alterada." : "Categoria criada." });
      aoFechar();
    },
    onError: (erro: unknown) => definirProblemas(problemasDaResposta(erro)),
  });

  const erroDe = (campo: string) => {
    const problema = problemas.find((item) => item.campo === campo);
    return problema ? `${problema.descricao} ${problema.sugestao}` : undefined;
  };

  const falhaSemMotivo = salvar.isError && problemas.length === 0;

  return (
    <Gaveta
      titulo={existente ? "Alterar categoria" : "Nova categoria"}
      descricao={existente ? existente.caminho : pai ? `Debaixo de ${pai.caminho}` : "Na raiz do plano de contas."}
      aberta={edicao !== null}
      aoFechar={aoFechar}
      rodape={
        <div className="flex justify-end gap-2">
          <Botao aparencia="secundario" type="button" onClick={aoFechar}>
            Cancelar
          </Botao>
          <Botao type="submit" form="formulario-de-categoria" disabled={salvar.isPending || nome.trim().length === 0}>
            {salvar.isPending ? "Salvando…" : existente ? "Salvar" : "Criar"}
          </Botao>
        </div>
      }
    >
      <form
        id="formulario-de-categoria"
        className="flex flex-col gap-4"
        onSubmit={(evento) => {
          evento.preventDefault();
          salvar.mutate();
        }}
      >
        {falhaSemMotivo && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            Não foi possível salvar. Tente de novo em instantes.
          </p>
        )}

        <Entrada
          rotulo="Nome"
          required
          value={nome}
          onChange={(evento) => definirNome(evento.target.value)}
          erro={erroDe("nome")}
          ajuda="Como o escritório fala do gasto ou da receita: “Aluguel e condomínio”, “Honorários”."
        />

        {existente || paiId ? (
          <p className="text-slate-600">
            Natureza: <strong>{natureza === "Pagar" ? "a pagar" : "a receber"}</strong>
            {existente ? ", e não muda depois de criada." : ", a mesma da categoria de cima."}
          </p>
        ) : (
          <Selecao
            rotulo="Natureza"
            value={natureza}
            onChange={(evento) => {
              definirNatureza(evento.target.value as NaturezaLancamento);
              definirPaiId("");
            }}
            erro={erroDe("natureza")}
          >
            <option value="Receber">A receber</option>
            <option value="Pagar">A pagar</option>
          </Selecao>
        )}

        <Selecao
          rotulo="Fica debaixo de"
          value={paiId}
          onChange={(evento) => definirPaiId(evento.target.value)}
          erro={erroDe("paiId")}
          ajuda={`Só aparecem os lugares onde ${existente?.temFilhas ? "ela e as que estão abaixo dela cabem" : "ela cabe"} nos três níveis.`}
        >
          <option value="">Na raiz</option>
          {lugares.map((categoria) => (
            <option key={categoria.id} value={categoria.id}>
              {categoria.caminho}
            </option>
          ))}
        </Selecao>

        {existente && (
          <label className="flex items-start gap-2 text-slate-700">
            <input
              type="checkbox"
              checked={ativa}
              onChange={(evento) => definirAtiva(evento.target.checked)}
              className="mt-1 size-4 rounded border-borda-forte accent-marca-600"
            />
            <span>
              Categoria ativa
              <span className="block text-sm text-slate-500">
                Inativa, deixa de ser oferecida para classificar, e o que já foi classificado nela continua lá.
              </span>
            </span>
          </label>
        )}
      </form>
    </Gaveta>
  );
}
