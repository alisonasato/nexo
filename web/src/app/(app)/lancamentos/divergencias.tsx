"use client";

import { useQuery } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import { formatarValor } from "@/lib/dinheiro";

/**
 * O aviso dos pagamentos que chegaram para títulos que não esperavam.
 *
 * <p>
 * O PSP avisa que o dinheiro entrou, mas o título já estava cancelado, baixado
 * à mão ou renegociado, ou não havia conta marcada para receber as cobranças.
 * O webhook marca, e não resolve: devolver ao cliente ou
 * reabrir o título é decisão de gente. Sem este aviso a marca ficaria só no
 * log, que é o mesmo que ninguém ver.
 * </p>
 * <p>
 * <b>Não aparece quando não há nada.</b> Um quadro vazio de "nenhuma
 * divergência" viraria parte da paisagem, e no dia em que houvesse uma ele já
 * não seria lido.
 * </p>
 */
export function AvisoDeDivergencias() {
  const divergencias = useQuery({
    queryKey: ["divergencias"],
    queryFn: async () => {
      const { data, error } = await api.GET("/cobrancas/divergencias");
      if (error || !data) throw new Error("Não foi possível conferir as divergências.");
      return data;
    },
  });

  const itens = divergencias.data ?? [];
  if (itens.length === 0) return null;

  const um = itens.length === 1;

  return (
    <section
      aria-labelledby="titulo-das-divergencias"
      className="rounded-[--radius-cartao] border border-amber-300 bg-amber-50 px-5 py-4 text-amber-950"
    >
      <h2 id="titulo-das-divergencias" className="font-semibold">
        {um
          ? "Um pagamento do PSP precisa de conferência"
          : `${itens.length} pagamentos do PSP precisam de conferência`}
      </h2>
      <p className="mt-1 text-amber-900">
        O dinheiro entrou pelo PSP e não pôde ser baixado sozinho: o título já não estava em aberto, ou
        não havia conta marcada para receber as cobranças. {um ? "O caso diz" : "Cada caso diz"} o motivo e
        o que fazer.
      </p>

      <details className="mt-3">
        <summary className="cursor-pointer font-medium underline-offset-2 hover:underline">
          {um ? "Ver o pagamento" : "Ver os pagamentos"}
        </summary>

        <ul className="mt-2 flex flex-col gap-2">
          {itens.map((item) => (
            <li key={item.eventoId} className="rounded-[--radius-controle] bg-white/70 px-3 py-2">
              <p className="font-medium">
                {item.nomeDaPessoa ?? "Lançamento que não existe mais"}
                {item.valor != null && (
                  <span className="numeros-tabulares font-normal"> · {formatarValor(item.valor)}</span>
                )}
              </p>
              {item.descricao && <p className="text-amber-900">{item.descricao}</p>}
              <p className="text-sm text-amber-800">{item.divergencia}</p>
            </li>
          ))}
        </ul>
      </details>
    </section>
  );
}
