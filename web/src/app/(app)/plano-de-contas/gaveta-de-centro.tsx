"use client";

import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { problemasDaResposta } from "@/lib/problemas";
import { useAvisos } from "@/componentes/avisos";
import { Botao, Entrada } from "@/componentes/controles";
import { Gaveta } from "@/componentes/gaveta";

type CentroNaLista = components["schemas"]["CentroNaLista"];
type Problema = components["schemas"]["Problema"];

type Props = { centro: CentroNaLista | "novo" | null; aoFechar: () => void };

/** Criar, renomear, inativar ou reativar um centro de custo. */
export function GavetaDeCentro({ centro, aoFechar }: Props) {
  const avisar = useAvisos();
  const clienteDeConsultas = useQueryClient();
  const existente = centro !== null && centro !== "novo" ? centro : null;

  const [nome, definirNome] = useState(existente?.nome ?? "");
  const [ativo, definirAtivo] = useState(existente?.ativo ?? true);
  const [problemas, definirProblemas] = useState<Problema[]>([]);

  const salvar = useMutation({
    mutationFn: async () => {
      const body = { nome, ativo };

      const { error } = existente
        ? await api.PUT("/centros-de-custo/{id}", { params: { path: { id: existente.id } }, body })
        : await api.POST("/centros-de-custo", { body });

      if (error) throw error;
    },
    onSuccess: () => {
      clienteDeConsultas.invalidateQueries({ queryKey: ["centros-de-custo"] });
      avisar({ tom: "sucesso", titulo: existente ? "Centro de custo alterado." : "Centro de custo criado." });
      aoFechar();
    },
    onError: (erro: unknown) => definirProblemas(problemasDaResposta(erro)),
  });

  const erroDoNome = problemas.find((problema) => problema.campo === "nome");
  const falhaSemMotivo = salvar.isError && problemas.length === 0;

  return (
    <Gaveta
      titulo={existente ? "Alterar centro de custo" : "Novo centro de custo"}
      descricao={existente ? existente.nome : "Uma unidade, área ou projeto do escritório."}
      aberta={centro !== null}
      aoFechar={aoFechar}
      rodape={
        <div className="flex justify-end gap-2">
          <Botao aparencia="secundario" type="button" onClick={aoFechar}>
            Cancelar
          </Botao>
          <Botao type="submit" form="formulario-de-centro" disabled={salvar.isPending || nome.trim().length === 0}>
            {salvar.isPending ? "Salvando…" : existente ? "Salvar" : "Criar"}
          </Botao>
        </div>
      }
    >
      <form
        id="formulario-de-centro"
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
          erro={erroDoNome ? `${erroDoNome.descricao} ${erroDoNome.sugestao}` : undefined}
          ajuda="“Matriz”, “Filial Centro”, “Departamento pessoal”."
        />

        {existente && (
          <label className="flex items-start gap-2 text-slate-700">
            <input
              type="checkbox"
              checked={ativo}
              onChange={(evento) => definirAtivo(evento.target.checked)}
              className="mt-1 size-4 rounded border-borda-forte accent-marca-600"
            />
            <span>
              Centro de custo ativo
              <span className="block text-sm text-slate-500">
                Inativo, deixa de ser oferecido, e o que já foi atribuído a ele continua lá.
              </span>
            </span>
          </label>
        )}
      </form>
    </Gaveta>
  );
}
