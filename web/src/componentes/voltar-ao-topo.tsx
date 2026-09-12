"use client";

import { useEffect, useState } from "react";

/**
 * O botão que devolve o começo da lista.
 *
 * <b>Só existe depois que há topo para voltar.</b> Aparecer com a página no
 * início seria um botão que não faz nada ocupando o canto — e num cadastro de
 * dez linhas ele nunca aparece, porque ali ninguém se perdeu.
 *
 * O foco vai junto com a rolagem. Sem isso, quem navega por teclado veria a
 * página subir e continuaria com o foco lá embaixo, tabulando às cegas pelo
 * fim da lista.
 */
export function VoltarAoTopo({ alvo = "conteudo" }: { alvo?: string }) {
  const [visivel, definirVisivel] = useState(false);

  useEffect(() => {
    /*
     * Aparece quando o cabeçalho da tela já saiu de vista — uns 320 pixels,
     * que é a altura do título mais a barra de busca. Medindo com treze
     * cadastros, a página inteira rola 294: ali não há para onde voltar, e o
     * botão corretamente não aparece.
     *
     * Passivo: este ouvinte nunca cancela a rolagem, e avisar isso ao
     * navegador tira o trabalho dele de esperar para descobrir.
     */
    const aoRolar = () => definirVisivel(window.scrollY > 320);

    aoRolar();
    window.addEventListener("scroll", aoRolar, { passive: true });
    return () => window.removeEventListener("scroll", aoRolar);
  }, []);

  function subir() {
    const semMovimento = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    window.scrollTo({ top: 0, behavior: semMovimento ? "auto" : "smooth" });

    const cabecalho = document.getElementById(alvo);
    cabecalho?.focus({ preventScroll: true });
  }

  return (
    <button
      type="button"
      onClick={subir}
      aria-hidden={!visivel}
      tabIndex={visivel ? 0 : -1}
      className={
        "fixed right-5 bottom-5 z-30 inline-flex min-h-11 items-center gap-2 rounded-full border border-borda " +
        "bg-superficie px-4 text-sm font-semibold text-slate-700 shadow-nivel-3 transition-all " +
        "hover:border-marca-400 hover:text-marca-700 " +
        (visivel ? "translate-y-0 opacity-100" : "pointer-events-none translate-y-2 opacity-0")
      }
    >
      <svg
        viewBox="0 0 24 24"
        aria-hidden="true"
        className="size-4"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="m6 14 6-6 6 6" />
      </svg>
      Ao topo
    </button>
  );
}
