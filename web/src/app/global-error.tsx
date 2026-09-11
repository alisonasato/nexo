"use client";

import { useEffect } from "react";

/**
 * O último recurso: erro no próprio layout raiz.
 *
 * Quando isto aparece, o layout que desenha `html` e `body` não montou — então
 * este arquivo precisa desenhar os dois. É também por isso que ele não usa os
 * componentes do sistema nem as classes do tema: se o que quebrou foi o
 * carregamento do CSS ou da fonte, depender deles faria a tela de erro quebrar
 * junto, e aí não sobra nada na tela.
 *
 * O estilo vai embutido, feio e garantido.
 */
export default function ErroGlobal({
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
    <html lang="pt-BR">
      <body
        style={{
          margin: 0,
          minHeight: "100vh",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          fontFamily: "system-ui, -apple-system, Segoe UI, sans-serif",
          background: "#f8fafc",
          color: "#0f172a",
        }}
      >
        <div style={{ maxWidth: "28rem", padding: "2rem", textAlign: "center" }}>
          <h1 style={{ fontSize: "1.25rem", margin: 0 }}>O Nexo não conseguiu carregar</h1>

          <p style={{ color: "#64748b", marginTop: "0.5rem" }}>
            A falha foi antes da aplicação montar. Recarregar costuma resolver; se
            insistir, o sistema pode estar fora do ar.
          </p>

          <button
            type="button"
            onClick={reset}
            style={{
              marginTop: "1.5rem",
              padding: "0.5rem 1rem",
              borderRadius: "0.5rem",
              border: "none",
              background: "#1e40af",
              color: "white",
              fontWeight: 600,
              cursor: "pointer",
            }}
          >
            Tentar de novo
          </button>

          {error.digest && (
            <p style={{ marginTop: "1.5rem", fontSize: "0.75rem", color: "#94a3b8" }}>
              Código do erro: {error.digest}
            </p>
          )}
        </div>
      </body>
    </html>
  );
}
