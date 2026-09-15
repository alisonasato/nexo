"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { useAvisos } from "@/componentes/avisos";
import { Botao, Entrada, EntradaMascarada, Selecao } from "@/componentes/controles";
import { Gaveta } from "@/componentes/gaveta";
import {
  competenciaAtual,
  formatarCompetencia,
  formatarData,
  formatarValor,
  hojeIso,
  mascararDinheiro,
} from "@/lib/dinheiro";
import { dividirEmParcelas, limitarParcelas, MAXIMO_DE_PARCELAS } from "@/lib/parcelas";

import { useCategorias, useCentrosDeCusto } from "./classificacao";

type NaturezaLancamento = components["schemas"]["NaturezaLancamento"];
type Problema = components["schemas"]["Problema"];

function problemasDaResposta(erro: unknown): Problema[] {
  if (erro && typeof erro === "object" && "problemas" in erro && Array.isArray(erro.problemas)) {
    return erro.problemas as Problema[];
  }
  return [];
}

/*
 * O que muda entre lançar a receber e a pagar: quem está do outro lado, e as
 * palavras. A lista oferece só quem a API aceita, cliente de um lado e
 * fornecedor do outro, para a recusa não aparecer só depois do clique.
 */
const lados = {
  Receber: {
    titulo: "Novo lançamento a receber",
    descricao: "Fora de contrato: declaração de IRPF, abertura de empresa, certidão.",
    papel: "Cliente",
    escolha: "Escolha um cliente…",
    oQue: "O que está sendo cobrado.",
  },
  Pagar: {
    titulo: "Novo lançamento a pagar",
    descricao: "As contas do escritório: aluguel, licença de sistema, serviço de terceiros.",
    papel: "Fornecedor",
    escolha: "Escolha um fornecedor…",
    oQue: "O que está sendo pago.",
  },
} as const;

/**
 * Quem abre passa uma chave com a natureza: trocar de aba começa um lançamento
 * do zero, e um cliente escolhido antes não segue adiante como fornecedor.
 */
type Props = { natureza: NaturezaLancamento; aberta: boolean; aoFechar: () => void };

/**
 * Lançar fora de contrato, inteiro ou em parcelas, a receber ou a pagar.
 *
 * <p>
 * Uma parcela só é o avulso de sempre; mais de uma divide o valor em parcelas
 * mensais, e a lista das parcelas aparece enquanto se digita.
 * </p>
 * <p>
 * <b>A competência é a mesma para todas as parcelas.</b> Ela é o mês do
 * serviço, e o serviço aconteceu uma vez: uma abertura de empresa paga em três
 * vezes continua sendo o trabalho de março.
 * </p>
 */
export function GavetaDeLancamento({ natureza, aberta, aoFechar }: Props) {
  const avisar = useAvisos();
  const clienteDeConsultas = useQueryClient();
  const competencia = competenciaAtual();
  const lado = lados[natureza];

  const [pessoa, definirPessoa] = useState("");
  const [descricao, definirDescricao] = useState("");
  const [digitos, definirDigitos] = useState("");
  const [parcelas, definirParcelas] = useState(1);
  const [primeiroVencimento, definirPrimeiroVencimento] = useState(hojeIso());
  const [ano, definirAno] = useState(competencia.ano);
  const [mes, definirMes] = useState(competencia.mes);
  const [problemas, definirProblemas] = useState<Problema[]>([]);
  const [categoria, definirCategoria] = useState("");
  const [centro, definirCentro] = useState("");

  /* Só o que a API aceitaria: categorias ativas desta natureza, centros ativos. */
  const categorias = (useCategorias().data ?? []).filter((item) => item.ativa && item.natureza === natureza);
  const centros = (useCentrosDeCusto().data ?? []).filter((item) => item.ativo);

  const pessoas = useQuery({
    queryKey: ["pessoas", lado.papel],
    enabled: aberta,
    queryFn: async () => {
      const { data, error } = await api.GET("/pessoas", {
        params: { query: { papel: lado.papel, tamanho: 200 } },
      });
      if (error || !data) throw new Error("Não foi possível carregar a lista de pessoas.");
      return data.itens;
    },
  });

  const centavos = Number(digitos || "0");
  const previa = dividirEmParcelas(centavos, parcelas, primeiroVencimento);

  function limpar() {
    definirPessoa("");
    definirDescricao("");
    definirDigitos("");
    definirParcelas(1);
    definirPrimeiroVencimento(hojeIso());
    definirProblemas([]);
  }

  const lancar = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST("/lancamentos/parcelamentos", {
        body: {
          natureza,
          pessoaId: pessoa,
          descricao,
          valorTotal: centavos / 100,
          parcelas,
          primeiroVencimento,
          competenciaAno: ano,
          competenciaMes: mes,
          categoriaId: categoria || null,
          centroDeCustoId: centro || null,
        },
      });
      if (error) throw error;
      return data!;
    },
    onSuccess: (criado) => {
      clienteDeConsultas.invalidateQueries({ queryKey: ["lancamentos"] });
      avisar({
        tom: "sucesso",
        titulo:
          criado.parcelas.length === 1
            ? "Lançamento criado."
            : `Lançado em ${criado.parcelas.length} parcelas.`,
      });
      limpar();
      aoFechar();
    },
    onError: (erro: unknown) => definirProblemas(problemasDaResposta(erro)),
  });

  const falhaSemMotivo = lancar.isError && problemas.length === 0;

  return (
    <Gaveta
      titulo={lado.titulo}
      descricao={lado.descricao}
      aberta={aberta}
      aoFechar={aoFechar}
      rodape={
        <div className="flex justify-end gap-2">
          <Botao aparencia="secundario" type="button" onClick={aoFechar}>
            Cancelar
          </Botao>
          <Botao
            type="submit"
            form="formulario-de-lancamento"
            disabled={lancar.isPending || !pessoa || descricao.trim().length === 0 || previa.length === 0}
          >
            {lancar.isPending ? "Lançando…" : parcelas > 1 ? `Lançar ${parcelas} parcelas` : "Lançar"}
          </Botao>
        </div>
      }
    >
      <form
        id="formulario-de-lancamento"
        className="flex flex-col gap-4"
        onSubmit={(evento) => {
          evento.preventDefault();
          lancar.mutate();
        }}
      >
        {(problemas.length > 0 || falhaSemMotivo) && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            {problemas.length > 0
              ? `${problemas[0].descricao} ${problemas[0].sugestao}`
              : "Não foi possível lançar. Tente de novo em instantes."}
          </p>
        )}

        <Selecao
          rotulo={lado.papel}
          required
          value={pessoa}
          onChange={(evento) => definirPessoa(evento.target.value)}
        >
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
          ajuda={lado.oQue}
        />

        <div className="grid grid-cols-2 gap-4">
          <EntradaMascarada
            rotulo="Valor total"
            required
            className="text-right"
            digitos={digitos}
            mascara={mascararDinheiro}
            aoMudar={definirDigitos}
            ajuda="Os centavos entram primeiro."
          />
          <Entrada
            rotulo="Parcelas"
            type="number"
            inputMode="numeric"
            min={1}
            max={MAXIMO_DE_PARCELAS}
            value={parcelas}
            onChange={(evento) => definirParcelas(limitarParcelas(evento.target.value))}
          />
        </div>

        <Entrada
          rotulo={parcelas > 1 ? "Primeiro vencimento" : "Vencimento"}
          type="date"
          required
          value={primeiroVencimento}
          onChange={(evento) => definirPrimeiroVencimento(evento.target.value)}
        />

        <div className="grid grid-cols-2 gap-4">
          <Selecao
            rotulo="Competência"
            value={mes}
            onChange={(evento) => definirMes(Number(evento.target.value))}
          >
            {Array.from({ length: 12 }, (_, indice) => (
              <option key={indice + 1} value={indice + 1}>
                {formatarCompetencia(ano, indice + 1).split("/")[0]}
              </option>
            ))}
          </Selecao>
          <Entrada
            rotulo="Ano"
            inputMode="numeric"
            value={ano}
            onChange={(evento) => definirAno(Number(evento.target.value) || competencia.ano)}
            ajuda="O mês do serviço, não o do vencimento."
          />
        </div>

        {/* Classificar é opcional: sem plano de contas, os campos nem aparecem. */}
        {categorias.length > 0 && (
          <Selecao rotulo="Categoria" value={categoria} onChange={(evento) => definirCategoria(evento.target.value)}>
            <option value="">Sem categoria</option>
            {categorias.map((item) => (
              <option key={item.id} value={item.id}>
                {item.caminho}
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
              </option>
            ))}
          </Selecao>
        )}

        {parcelas > 1 && previa.length > 0 && (
          <div className="rounded-[--radius-controle] border border-borda">
            <p className="border-b border-borda px-4 py-2 text-xs font-semibold tracking-wide text-slate-500 uppercase">
              As parcelas
            </p>
            <ol className="divide-y divide-borda">
              {previa.map((parcela) => (
                <li key={parcela.numero} className="numeros-tabulares flex justify-between gap-3 px-4 py-2">
                  <span className="text-slate-600">
                    {parcela.numero} de {previa.length} · vence {formatarData(parcela.vencimento)}
                  </span>
                  <span className="font-medium text-slate-800">{formatarValor(parcela.centavos / 100)}</span>
                </li>
              ))}
            </ol>
          </div>
        )}
      </form>
    </Gaveta>
  );
}
