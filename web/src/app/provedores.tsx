"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";

/**
 * A camada assíncrona do front (decisão Q29).
 *
 * Ela existe desde o primeiro dia de propósito: o protótipo anterior nasceu
 * sobre um store síncrono e todo formulário acabou assumindo que o dado já
 * estava lá. Aqui nada está lá antes de chegar.
 */
function criarCliente(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        /** Trinta segundos: tela de ERP relê muito e muda pouco entre cliques. */
        staleTime: 30_000,
        retry: 1,
      },
    },
  });
}

let clienteDoNavegador: QueryClient | undefined;

function obterCliente(): QueryClient {
  /*
   * No servidor, cada render precisa do seu próprio cliente: um cliente
   * compartilhado entre requisições faria o dado de um usuário aparecer no
   * render de outro. No navegador, o contrário — um só, para o cache
   * sobreviver à navegação entre telas.
   */
  if (typeof window === "undefined") return criarCliente();
  clienteDoNavegador ??= criarCliente();
  return clienteDoNavegador;
}

export function Provedores({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={obterCliente()}>{children}</QueryClientProvider>;
}
