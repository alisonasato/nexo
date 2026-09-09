"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";

import { Botao, Entrada } from "@/componentes/controles";
import { useEntrar, useSessao } from "@/sessao/usar-sessao";

export default function PaginaDeEntrada() {
  const navegacao = useRouter();
  const sessao = useSessao();
  const entrar = useEntrar();

  const [email, definirEmail] = useState("");
  const [senha, definirSenha] = useState("");

  /* Quem já entrou não precisa ver esta tela. */
  useEffect(() => {
    if (sessao.data) navegacao.replace("/pessoas");
  }, [sessao.data, navegacao]);

  return (
    <main className="flex min-h-dvh items-center justify-center px-6 py-12">
      <div className="w-full max-w-sm">
        <header className="mb-8 flex flex-col gap-1">
          <h1 className="text-2xl font-semibold tracking-tight text-marca-950">Nexo</h1>
          <p className="text-slate-500">Entre para continuar.</p>
        </header>

        <form
          className="flex flex-col gap-5 rounded-[--radius-cartao] border border-borda bg-superficie p-6 shadow-nivel-2"
          onSubmit={(evento) => {
            evento.preventDefault();
            entrar.mutate(
              { email, senha },
              { onSuccess: () => navegacao.replace("/pessoas") },
            );
          }}
        >
          <Entrada
            rotulo="E-mail"
            type="email"
            name="email"
            autoComplete="username"
            required
            autoFocus
            value={email}
            onChange={(evento) => definirEmail(evento.target.value)}
          />

          <Entrada
            rotulo="Senha"
            type="password"
            name="senha"
            autoComplete="current-password"
            required
            value={senha}
            onChange={(evento) => definirSenha(evento.target.value)}
          />

          {entrar.isError && (
            /*
             * `role="alert"` para o leitor de tela anunciar a falha: quem não
             * enxerga a tela precisa saber que a tentativa foi recusada.
             */
            <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-3 py-2 text-red-700">
              {entrar.error.message}
            </p>
          )}

          <Botao type="submit" disabled={entrar.isPending}>
            {entrar.isPending ? "Entrando…" : "Entrar"}
          </Botao>
        </form>
      </div>
    </main>
  );
}
