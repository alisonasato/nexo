"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";

/**
 * Quem está na sessão, ou `null` quando ninguém está.
 *
 * O 401 aqui **não é erro**: é a resposta legítima para "ninguém entrou". Se
 * ele virasse exceção, cada visita à tela de login registraria uma falha e a
 * tentativa seria repetida à toa. Erro é quando a API não responde.
 *
 * A sessão vive no cookie httpOnly, que o JavaScript não lê. Então a única
 * forma de saber quem está logado é perguntar à API — e é isto que esta
 * consulta faz.
 */
export function useSessao() {
  return useQuery({
    queryKey: ["sessao"],
    queryFn: async () => {
      const { data, response } = await api.GET("/autenticacao/eu");
      if (response.status === 401) return null;
      if (!data) throw new Error("Não foi possível confirmar a sessão.");
      return data;
    },
    retry: false,
    staleTime: 5 * 60 * 1000,
  });
}

export function useEntrar() {
  const clienteDeConsultas = useQueryClient();

  return useMutation({
    mutationFn: async (credenciais: { email: string; senha: string }) => {
      const { data, error, response } = await api.POST("/autenticacao/entrar", {
        body: credenciais,
      });

      if (response.status === 423) {
        throw new Error(
          "Conta bloqueada por tentativas seguidas. Tente de novo em alguns minutos.",
        );
      }

      if (error || !data) throw new Error("E-mail ou senha incorretos.");

      return data;
    },
    onSuccess: () => clienteDeConsultas.invalidateQueries({ queryKey: ["sessao"] }),
  });
}

export function useSair() {
  const clienteDeConsultas = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      await api.POST("/autenticacao/sair");
    },
    /*
     * Limpa tudo, não só a sessão: o cache guarda dados do tenant de quem
     * saiu, e a próxima pessoa a entrar nesta máquina não pode vê-los
     * piscando na tela antes da primeira busca.
     */
    onSuccess: () => clienteDeConsultas.clear(),
  });
}
