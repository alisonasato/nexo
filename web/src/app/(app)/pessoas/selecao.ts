"use client";

import { useState } from "react";

/**
 * A seleção de linhas, amarrada à consulta que a produziu.
 *
 * <b>Seleção pertence à página que está na tela.</b> Marcar três pessoas, mudar
 * de página e mandar inativar precisa inativar aquelas três — não três linhas
 * de outra lista que por acaso ocupam as mesmas posições. Guardar ids soltos
 * sobreviveria à troca de página e produziria exatamente esse acidente.
 *
 * A amarração é feita por derivação, e não por um efeito que limpa quando a
 * consulta muda: efeito roda depois da pintura, e existiria um quadro em que a
 * barra de ações mostra "3 selecionadas" sobre a lista nova. Aqui a seleção de
 * outra consulta simplesmente não é lida.
 */
export function useSelecaoPorConsulta(chaveDaConsulta: string) {
  const [guardada, definirGuardada] = useState<{ chave: string; ids: string[] }>({
    chave: chaveDaConsulta,
    ids: [],
  });

  const ids = guardada.chave === chaveDaConsulta ? guardada.ids : [];

  const definir = (novos: string[]) =>
    definirGuardada({ chave: chaveDaConsulta, ids: novos });

  return {
    ids,
    marcadas: new Set(ids),
    quantidade: ids.length,
    alternar: (id: string) =>
      definir(ids.includes(id) ? ids.filter((outro) => outro !== id) : [...ids, id]),
    somente: (id: string) => definir([id]),
    trocarTodas: (todos: string[]) => definir(ids.length === todos.length ? [] : todos),
    limpar: () => definir([]),
  };
}
