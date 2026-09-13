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

type Problema = components["schemas"]["Problema"];

function problemasDaResposta(erro: unknown): Problema[] {
  if (erro && typeof erro === "object" && "problemas" in erro && Array.isArray(erro.problemas)) {
    return erro.problemas as Problema[];
  }
  return [];
}

type Props = { aberta: boolean; aoFechar: () => void };

/**
 * Lançar a receber fora de contrato, inteiro ou em parcelas.
 *
 * <p>
 * Substitui o formulário de cobrança avulsa que ficava aberto no alto da tela.
 * Uma parcela só é o avulso de sempre; mais de uma divide o valor em parcelas
 * mensais, e a lista das parcelas aparece enquanto se digita.
 * </p>
 * <p>
 * <b>A competência é a mesma para todas as parcelas.</b> Ela é o mês do
 * serviço, e o serviço aconteceu uma vez: uma abertura de empresa paga em três
 * vezes continua sendo o trabalho de março.
 * </p>
 */
export function GavetaDeLancamento({ aberta, aoFechar }: Props) {
  const avisar = useAvisos();
  const clienteDeConsultas = useQueryClient();
  const competencia = competenciaAtual();

  const [cliente, definirCliente] = useState("");
  const [descricao, definirDescricao] = useState("");
  const [digitos, definirDigitos] = useState("");
  const [parcelas, definirParcelas] = useState(1);
  const [primeiroVencimento, definirPrimeiroVencimento] = useState(hojeIso());
  const [ano, definirAno] = useState(competencia.ano);
  const [mes, definirMes] = useState(competencia.mes);
  const [problemas, definirProblemas] = useState<Problema[]>([]);

  /* Só quem carrega o papel de cliente: cobrar um fornecedor por engano de
     escolha na lista é erro que só aparece na hora de receber. */
  const clientes = useQuery({
    queryKey: ["pessoas", "clientes"],
    enabled: aberta,
    queryFn: async () => {
      const { data, error } = await api.GET("/pessoas", {
        params: { query: { papel: "Cliente", tamanho: 200 } },
      });
      if (error || !data) throw new Error("Não foi possível carregar os clientes.");
      return data.itens;
    },
  });

  const centavos = Number(digitos || "0");
  const previa = dividirEmParcelas(centavos, parcelas, primeiroVencimento);

  function limpar() {
    definirCliente("");
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
          pessoaId: cliente,
          descricao,
          valorTotal: centavos / 100,
          parcelas,
          primeiroVencimento,
          competenciaAno: ano,
          competenciaMes: mes,
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
      titulo="Novo lançamento"
      descricao="A receber, fora de contrato: declaração de IRPF, abertura de empresa, certidão."
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
            disabled={lancar.isPending || !cliente || descricao.trim().length === 0 || previa.length === 0}
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
          rotulo="Cliente"
          required
          value={cliente}
          onChange={(evento) => definirCliente(evento.target.value)}
        >
          <option value="">{clientes.isPending ? "Carregando…" : "Escolha um cliente…"}</option>
          {clientes.data?.map((item) => (
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
          ajuda="O que está sendo cobrado."
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
