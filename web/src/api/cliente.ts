import createClient from "openapi-fetch";

import type { paths } from "./esquema";

/**
 * O cliente HTTP do front.
 *
 * Os tipos vêm de `esquema.d.ts`, gerado a partir do documento OpenAPI da API
 * — nunca escrito à mão (decisão Q19). É isso que faz duas linguagens custarem
 * o build em vez do dobro: se um endpoint mudar de forma e o front não
 * acompanhar, o `tsc` reclama antes do navegador.
 *
 * O endereço é **relativo**. A API é servida no mesmo domínio do front, sob
 * `/api`, por uma reescrita de rota configurada em `next.config.ts` — o
 * navegador nunca sabe onde a API realmente está. Foi assim que a exigência de
 * mesmo domínio da decisão Q29 deixou de depender de alguém configurar direito
 * na hora de implantar.
 *
 * `credentials: "include"` continua aqui por precisão, ainda que mesma origem
 * já envie o cookie: a intenção fica dita, e a linha não passa a mentir se um
 * dia o endereço voltar a ser absoluto.
 */
export const api = createClient<paths>({
  baseUrl: "/api",
  credentials: "include",
});
