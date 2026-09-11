"use client";

import { useEffect } from "react";

import { TelaDeErro } from "@/componentes/tela-de-erro";

/**
 * Erro dentro das telas autenticadas.
 *
 * Fica aqui, e não na raiz, porque o Next monta o limite de erro mais próximo:
 * assim a casca continua na tela — menu, botão de sair, tudo —, e a pessoa cai
 * numa página que ainda é o sistema. Um erro em Recebíveis não deveria tirar
 * ninguém de Recebíveis.
 */
export default function ErroNaAplicacao({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    /* O detalhe vai para o console, não para a tela. Em produção o Next também
       registra do lado do servidor, com o mesmo digest que aparece embaixo. */
    console.error(error);
  }, [error]);

  return (
    <TelaDeErro
      titulo="Esta tela não carregou"
      descricao="Alguma coisa quebrou no meio do caminho. Seus dados não foram alterados."
      tentarDeNovo={reset}
      digest={error.digest}
    />
  );
}
