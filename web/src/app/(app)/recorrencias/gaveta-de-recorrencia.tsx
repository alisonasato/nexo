"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { problemasDaResposta } from "@/lib/problemas";
import { useAvisos } from "@/componentes/avisos";
import { Botao, Entrada, EntradaMascarada, Selecao } from "@/componentes/controles";
import { Gaveta } from "@/componentes/gaveta";
import { digitosDoValor, hojeIso, mascararDinheiro, valorDosDigitos } from "@/lib/dinheiro";

import { useCategorias, useCentrosDeCusto } from "../lancamentos/classificacao";

type RecorrenciaNaLista = components["schemas"]["RecorrenciaNaLista"];
type NaturezaLancamento = components["schemas"]["NaturezaLancamento"];
type FrequenciaDeRecorrencia = components["schemas"]["FrequenciaDeRecorrencia"];
type Problema = components["schemas"]["Problema"];

/** Nula com a gaveta fechada. Quem abre passa uma chave, para cada abertura começar do que está gravado. */
export type EdicaoDeRecorrencia = { tipo: "nova" } | { tipo: "alterar"; recorrencia: RecorrenciaNaLista };

export const rotulosDeFrequencia: Record<FrequenciaDeRecorrencia, string> = {
  Mensal: "Mensal",
  Trimestral: "Trimestral",
  Semestral: "Semestral",
  Anual: "Anual",
};

/* Quem está do outro lado muda com a natureza, e a lista só oferece quem a API aceita. */
const lados = {
  Receber: { rotulo: "A receber", papel: "Cliente", escolha: "Escolha um cliente…" },
  Pagar: { rotulo: "A pagar", papel: "Fornecedor", escolha: "Escolha um fornecedor…" },
} as const;

type Props = { edicao: EdicaoDeRecorrencia | null; aoFechar: () => void };

/**
 * Cadastrar ou alterar uma recorrência.
 *
 * <p>
 * <b>A natureza só se escolhe ao cadastrar.</b> Trocá-la depois deixaria os
 * lançamentos já gerados de um lado e os próximos do outro, sob o mesmo nome.
 * </p>
 * <p>
 * Alterar vale para as próximas gerações: o que já foi gerado fica como está, e
 * se corrige no próprio lançamento.
 * </p>
 */
export function GavetaDeRecorrencia({ edicao, aoFechar }: Props) {
  const avisar = useAvisos();
  const clienteDeConsultas = useQueryClient();
  const existente = edicao?.tipo === "alterar" ? edicao.recorrencia : null;

  const [natureza, definirNatureza] = useState<NaturezaLancamento>(existente?.natureza ?? "Pagar");
  const [pessoa, definirPessoa] = useState(existente?.pessoaId ?? "");
  const [descricao, definirDescricao] = useState(existente?.descricao ?? "");
  const [digitos, definirDigitos] = useState(digitosDoValor(existente?.valor));
  const [frequencia, definirFrequencia] = useState<FrequenciaDeRecorrencia>(existente?.frequencia ?? "Mensal");
  const [dia, definirDia] = useState(String(existente?.diaDeVencimento ?? 10));
  const [inicio, definirInicio] = useState(existente?.inicioEm ?? hojeIso());
  const [fim, definirFim] = useState(existente?.fimEm ?? "");
  const [categoria, definirCategoria] = useState(existente?.categoriaId ?? "");
  const [centro, definirCentro] = useState(existente?.centroDeCustoId ?? "");
  const [ativa, definirAtiva] = useState(existente?.ativa ?? true);
  const [problemas, definirProblemas] = useState<Problema[]>([]);

  const lado = lados[natureza];

  /* Só o que a API aceitaria, mais o que já estava gravado, que passa mesmo inativo. */
  const categorias = (useCategorias().data ?? []).filter(
    (item) => item.natureza === natureza && (item.ativa || item.id === existente?.categoriaId),
  );
  const centros = (useCentrosDeCusto().data ?? []).filter(
    (item) => item.ativo || item.id === existente?.centroDeCustoId,
  );

  const pessoas = useQuery({
    queryKey: ["pessoas", lado.papel],
    enabled: edicao !== null,
    queryFn: async () => {
      const { data, error } = await api.GET("/pessoas", {
        params: { query: { papel: lado.papel, tamanho: 200 } },
      });
      if (error || !data) throw new Error("Não foi possível carregar a lista de pessoas.");
      return data.itens;
    },
  });

  const gravar = useMutation({
    mutationFn: async () => {
      const body = {
        natureza,
        pessoaId: pessoa,
        descricao,
        valor: valorDosDigitos(digitos),
        frequencia,
        diaDeVencimento: Number(dia),
        inicioEm: inicio,
        fimEm: fim || null,
        categoriaId: categoria || null,
        centroDeCustoId: centro || null,
        ativa,
      };

      const { error } = existente
        ? await api.PUT("/recorrencias/{id}", { params: { path: { id: existente.id } }, body })
        : await api.POST("/recorrencias", { body });
      if (error) throw error;
    },
    onSuccess: () => {
      clienteDeConsultas.invalidateQueries({ queryKey: ["recorrencias"] });
      avisar({ tom: "sucesso", titulo: existente ? "Recorrência alterada." : "Recorrência cadastrada." });
      aoFechar();
    },
    onError: (erro: unknown) => definirProblemas(problemasDaResposta(erro)),
  });

  const falhaSemMotivo = gravar.isError && problemas.length === 0;

  return (
    <Gaveta
      titulo={existente ? "Alterar recorrência" : "Nova recorrência"}
      descricao={
        existente
          ? existente.descricao
          : "Gera um lançamento cada vez que cai na competência, quando o mês é gerado."
      }
      aberta={edicao !== null}
      aoFechar={aoFechar}
      rodape={
        <div className="flex justify-end gap-2">
          <Botao aparencia="secundario" type="button" onClick={aoFechar}>
            Cancelar
          </Botao>
          <Botao
            type="submit"
            form="formulario-de-recorrencia"
            disabled={gravar.isPending || !pessoa || descricao.trim().length === 0 || digitos.length === 0}
          >
            {gravar.isPending ? "Salvando…" : "Salvar"}
          </Botao>
        </div>
      }
    >
      <form
        id="formulario-de-recorrencia"
        className="flex flex-col gap-4"
        onSubmit={(evento) => {
          evento.preventDefault();
          gravar.mutate();
        }}
      >
        {(problemas.length > 0 || falhaSemMotivo) && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            {problemas.length > 0
              ? `${problemas[0].descricao} ${problemas[0].sugestao}`
              : "Não foi possível salvar. Tente de novo em instantes."}
          </p>
        )}

        {existente ? (
          <p className="text-slate-700">
            <span className="font-semibold">{lado.rotulo}.</span> A natureza não muda depois de criada.
          </p>
        ) : (
          <Selecao
            rotulo="Natureza"
            value={natureza}
            onChange={(evento) => {
              /* Do outro lado, o cliente escolhido não é fornecedor, e a categoria é de outra árvore. */
              definirNatureza(evento.target.value as NaturezaLancamento);
              definirPessoa("");
              definirCategoria("");
            }}
          >
            <option value="Pagar">A pagar</option>
            <option value="Receber">A receber</option>
          </Selecao>
        )}

        <Selecao rotulo={lado.papel} required value={pessoa} onChange={(evento) => definirPessoa(evento.target.value)}>
          <option value="">{pessoas.isPending ? "Carregando…" : lado.escolha}</option>
          {pessoas.data?.map((item) => (
            <option key={item.id} value={item.id}>
              {item.codigo} · {item.nomeFantasia || item.nome}
            </option>
          ))}
        </Selecao>

        <Entrada
          rotulo="Descrição"
          required
          value={descricao}
          onChange={(evento) => definirDescricao(evento.target.value)}
          ajuda="“Aluguel do escritório”, “Licença do sistema contábil”."
        />

        <div className="grid grid-cols-2 gap-4">
          <EntradaMascarada
            rotulo="Valor"
            required
            className="text-right"
            digitos={digitos}
            mascara={mascararDinheiro}
            aoMudar={definirDigitos}
            ajuda="De cada lançamento gerado."
          />
          <Selecao
            rotulo="Frequência"
            value={frequencia}
            onChange={(evento) => definirFrequencia(evento.target.value as FrequenciaDeRecorrencia)}
          >
            {Object.entries(rotulosDeFrequencia).map(([valor, rotulo]) => (
              <option key={valor} value={valor}>
                {rotulo}
              </option>
            ))}
          </Selecao>
        </div>

        <div className="grid grid-cols-2 gap-4">
          <Entrada
            rotulo="Dia do vencimento"
            type="number"
            inputMode="numeric"
            min={1}
            max={31}
            required
            value={dia}
            onChange={(evento) => definirDia(evento.target.value)}
            ajuda="Nos meses curtos, o último dia."
          />
          <Entrada
            rotulo="Início"
            type="date"
            required
            value={inicio}
            onChange={(evento) => definirInicio(evento.target.value)}
            ajuda="O mês dele é a primeira competência."
          />
        </div>

        <Entrada
          rotulo="Fim"
          type="date"
          value={fim}
          onChange={(evento) => definirFim(evento.target.value)}
          ajuda="Opcional. Depois dele, a recorrência para de gerar."
        />

        {/* Classificar é opcional: sem plano de contas, os campos nem aparecem. */}
        {categorias.length > 0 && (
          <Selecao rotulo="Categoria" value={categoria} onChange={(evento) => definirCategoria(evento.target.value)}>
            <option value="">Sem categoria</option>
            {categorias.map((item) => (
              <option key={item.id} value={item.id}>
                {item.caminho}
                {item.ativa ? "" : " (inativa)"}
              </option>
            ))}
          </Selecao>
        )}

        {centros.length > 0 && (
          <Selecao rotulo="Centro de custo" value={centro} onChange={(evento) => definirCentro(evento.target.value)}>
            <option value="">Sem centro de custo</option>
            {centros.map((item) => (
              <option key={item.id} value={item.id}>
                {item.nome}
                {item.ativo ? "" : " (inativo)"}
              </option>
            ))}
          </Selecao>
        )}

        {existente && (
          <label className="flex items-start gap-2 text-slate-700">
            <input
              type="checkbox"
              checked={ativa}
              onChange={(evento) => definirAtiva(evento.target.checked)}
              className="mt-1 size-4 rounded border-borda-forte accent-marca-600"
            />
            <span>
              Recorrência ativa
              <span className="block text-sm text-slate-500">
                Inativa, deixa de gerar, e o que ela já gerou continua lá.
              </span>
            </span>
          </label>
        )}
      </form>
    </Gaveta>
  );
}
