import { useQuery } from "@tanstack/react-query";

import { api } from "@/api/cliente";

/**
 * As categorias do plano de contas, na ordem de árvore e com o caminho.
 *
 * A chave é a mesma da tela do plano: uma categoria criada lá aparece aqui sem
 * recarregar, e quem filtra por natureza ou por ativa é quem usa.
 */
export function useCategorias() {
  return useQuery({
    queryKey: ["categorias"],
    queryFn: async () => {
      const { data, error } = await api.GET("/categorias");
      if (error || !data) throw new Error("Não foi possível carregar as categorias.");
      return data;
    },
  });
}

/** Os centros de custo, com a mesma chave da tela do plano. */
export function useCentrosDeCusto() {
  return useQuery({
    queryKey: ["centros-de-custo"],
    queryFn: async () => {
      const { data, error } = await api.GET("/centros-de-custo");
      if (error || !data) throw new Error("Não foi possível carregar os centros de custo.");
      return data;
    },
  });
}
