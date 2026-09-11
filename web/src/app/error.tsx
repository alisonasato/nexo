"use client";

import { useEffect } from "react";

import { TelaDeErro } from "@/componentes/tela-de-erro";

/**
 * Erro fora das telas autenticadas — na prática, a de entrar.
 *
 * Existe porque sem ele um erro ali cairia no limite global, que precisa
 * desenhar a página do zero e sai sem nada do sistema. Quem não consegue nem
 * entrar já está com um problema; a tela não precisa piorar parecendo quebrada.
 */
export default function ErroNaRaiz({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error(error);
  }, [error]);

  return (
    <div className="flex min-h-dvh flex-col justify-center">
      <TelaDeErro
        titulo="Não foi possível abrir esta página"
        descricao="Alguma coisa quebrou antes de a página montar. Tentar de novo costuma resolver."
        tentarDeNovo={reset}
        digest={error.digest}
      />
    </div>
  );
}
