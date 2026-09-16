import type { components } from "@/api/esquema";

type Problema = components["schemas"]["Problema"];

/** Os problemas de uma recusa da API. Vazio quando a falha não trouxe nenhum — rede fora, 500. */
export function problemasDaResposta(erro: unknown): Problema[] {
  if (erro && typeof erro === "object" && "problemas" in erro && Array.isArray(erro.problemas)) {
    return erro.problemas as Problema[];
  }
  return [];
}
