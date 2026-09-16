import { queryOptions, useQuery } from "@tanstack/react-query";

import { api } from "@/api/cliente";

/* A consulta das contas, com a mesma chave em todo lugar que as lê. */
const consultaDasContas = queryOptions({
  queryKey: ["contas-bancarias"],
  queryFn: async () => {
    const { data, error } = await api.GET("/contas-bancarias");
    if (error || !data) throw new Error("Não foi possível carregar as contas.");
    return data;
  },
});

/**
 * As contas que podem receber uma baixa: as ativas.
 *
 * A consulta é a mesma da tela de contas, com a mesma chave, e só o recorte
 * muda. Uma conta cadastrada lá aparece aqui sem recarregar, e a baixa que
 * muda o saldo invalida as duas telas de uma vez.
 */
export function useContasParaBaixa() {
  return useQuery({ ...consultaDasContas, select: (contas) => contas.filter((conta) => conta.ativa) });
}

/** Todas as contas, ativas ou não: o histórico dá nome também à conta que já foi encerrada. */
export function useContasBancarias() {
  return useQuery(consultaDasContas);
}
