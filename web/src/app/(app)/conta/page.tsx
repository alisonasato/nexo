"use client";

import { useState } from "react";
import { useMutation } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import { Botao, Entrada } from "@/componentes/controles";
import type { components } from "@/api/esquema";
import { useSessao } from "@/sessao/usar-sessao";

type Problema = components["schemas"]["Problema"];

function problemasDaResposta(erro: unknown): Problema[] {
  if (erro && typeof erro === "object" && "problemas" in erro && Array.isArray(erro.problemas)) {
    return erro.problemas as Problema[];
  }
  return [];
}

export default function Conta() {
  const sessao = useSessao();

  const [senhaAtual, definirSenhaAtual] = useState("");
  const [senhaNova, definirSenhaNova] = useState("");
  const [repetida, definirRepetida] = useState("");
  const [problemas, definirProblemas] = useState<Problema[]>([]);
  const [trocada, definirTrocada] = useState(false);

  const trocar = useMutation({
    mutationFn: async () => {
      const { error } = await api.POST("/autenticacao/trocar-senha", {
        body: { senhaAtual, senhaNova },
      });
      if (error) throw error;
    },
    onSuccess: () => {
      definirProblemas([]);
      definirSenhaAtual("");
      definirSenhaNova("");
      definirRepetida("");
      definirTrocada(true);
    },
    onError: (erro) => {
      definirTrocada(false);
      definirProblemas(problemasDaResposta(erro));
    },
  });

  const erroDe = (campo: string) => problemas.find((p) => p.campo === campo)?.descricao;
  const sugestaoDe = (campo: string) => problemas.find((p) => p.campo === campo)?.sugestao;

  /*
   * A conferência da repetição é só daqui: serve para pegar erro de digitação
   * antes de gastar uma ida ao servidor. Quem decide se a senha vale é a API.
   */
  const naoConfere = repetida.length > 0 && senhaNova !== repetida;

  return (
    <>
      <header className="border-b border-borda bg-superficie px-6 py-4">
        <h1 className="text-xl font-semibold tracking-tight text-marca-950">Minha conta</h1>
        <p className="text-slate-600">{sessao.data?.email}</p>
      </header>

      <div className="flex flex-1 flex-col gap-6 p-6">
        <form
          className="flex max-w-md flex-col gap-5 rounded-[--radius-cartao] border border-borda bg-superficie p-5"
          onSubmit={(evento) => {
            evento.preventDefault();
            trocar.mutate();
          }}
        >
          <div>
            <h2 className="font-semibold text-marca-950">Trocar a senha</h2>
            <p className="text-slate-600">
              A senha atual é pedida mesmo com a sessão aberta: sessão aberta prova que alguém
              entrou, não que continua sendo você.
            </p>
          </div>

          {trocada && (
            <p
              role="status"
              className="rounded-[--radius-controle] bg-emerald-50 px-3 py-2 text-emerald-800"
            >
              Senha trocada. A anterior deixou de valer.
            </p>
          )}

          <Entrada
            rotulo="Senha atual"
            type="password"
            autoComplete="current-password"
            required
            value={senhaAtual}
            onChange={(evento) => definirSenhaAtual(evento.target.value)}
            erro={erroDe("senhaAtual")}
            ajuda={sugestaoDe("senhaAtual")}
          />

          <Entrada
            rotulo="Senha nova"
            type="password"
            autoComplete="new-password"
            required
            value={senhaNova}
            onChange={(evento) => definirSenhaNova(evento.target.value)}
            erro={erroDe("senhaNova")}
            ajuda={sugestaoDe("senhaNova") ?? "Ao menos 10 caracteres, com número, maiúscula e símbolo."}
          />

          <Entrada
            rotulo="Repita a senha nova"
            type="password"
            autoComplete="new-password"
            required
            value={repetida}
            onChange={(evento) => definirRepetida(evento.target.value)}
            erro={naoConfere ? "As duas não são iguais." : undefined}
          />

          <Botao
            type="submit"
            disabled={trocar.isPending || naoConfere || !senhaAtual || !senhaNova}
          >
            {trocar.isPending ? "Trocando…" : "Trocar a senha"}
          </Botao>
        </form>

        <p className="max-w-md text-slate-600">
          Uma sessão aberta em outro navegador continua funcionando até expirar — o token não é
          consultado no banco a cada requisição. Se a troca for por suspeita de vazamento, saia de
          todos os aparelhos que você conseguir alcançar.
        </p>
      </div>
    </>
  );
}
