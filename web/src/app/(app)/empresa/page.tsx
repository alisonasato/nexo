"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import { Botao, Entrada, EntradaMascarada } from "@/componentes/controles";
import type { components } from "@/api/esquema";
import { mascararCnpj } from "@/lib/mascaras";

type Problema = components["schemas"]["Problema"];

type Campos = { razaoSocial: string; nomeFantasia: string; cnpj: string };

function problemasDaResposta(erro: unknown): Problema[] {
  if (erro && typeof erro === "object" && "problemas" in erro && Array.isArray(erro.problemas)) {
    return erro.problemas as Problema[];
  }
  return [];
}

/**
 * O estabelecimento do próprio escritório.
 *
 * Existe porque o provisionamento grava razão social e CNPJ uma vez, lendo
 * variável de ambiente, e não conferia nada. Um dígito trocado ali ficava
 * gravado para sempre: reprovisionar não resolve, porque só age com o banco
 * vazio, e não havia tela nenhuma que alcançasse esse dado.
 */
export default function EmpresaDoEscritorio() {
  const clienteDeConsultas = useQueryClient();

  const [problemas, definirProblemas] = useState<Problema[]>([]);
  const [salva, definirSalva] = useState(false);

  /*
   * Nulo até alguém digitar. Enquanto for nulo, o formulário mostra o que veio
   * do servidor; a partir da primeira tecla, mostra o rascunho.
   *
   * O caminho óbvio seria um efeito copiando a consulta para três estados
   * quando ela chega. Além de o React desaconselhar, ele reintroduz o problema
   * que já existe nos outros formulários: o efeito dispara de novo a cada
   * chegada de dado e passa por cima do que a pessoa estava digitando.
   */
  const [rascunho, definirRascunho] = useState<Campos | null>(null);

  const empresas = useQuery({
    queryKey: ["empresas"],
    queryFn: async () => {
      const { data, error } = await api.GET("/empresas");
      if (error || !data) throw new Error("Não foi possível carregar a empresa.");
      return data;
    },
  });

  /*
   * Hoje o escritório tem uma empresa só: o provisionamento cria a matriz e
   * não existe caminho para abrir filial. A tela assume isso em vez de
   * inventar um seletor que escolheria entre uma opção — e quando filial
   * existir, é aqui que ele entra.
   */
  const empresa = empresas.data?.[0];

  const valores: Campos = rascunho ?? {
    razaoSocial: empresa?.razaoSocial ?? "",
    nomeFantasia: empresa?.nomeFantasia ?? "",
    cnpj: empresa?.cnpj ?? "",
  };

  function alterar<C extends keyof Campos>(campo: C, valor: Campos[C]) {
    definirRascunho({ ...valores, [campo]: valor });
    definirSalva(false);
  }

  const salvar = useMutation({
    mutationFn: async () => {
      const { error } = await api.PUT("/empresas/{id}", {
        params: { path: { id: empresa!.id } },
        body: valores,
      });
      if (error) throw error;
    },
    onSuccess: () => {
      definirProblemas([]);
      definirSalva(true);

      /* Some com o rascunho: o que o servidor devolver passa a ser a verdade. */
      definirRascunho(null);
      clienteDeConsultas.invalidateQueries({ queryKey: ["empresas"] });
    },
    onError: (erro) => {
      definirSalva(false);
      definirProblemas(problemasDaResposta(erro));
    },
  });

  const erroDe = (campo: string) => problemas.find((p) => p.campo === campo)?.descricao;
  const sugestaoDe = (campo: string) => problemas.find((p) => p.campo === campo)?.sugestao;

  return (
    <>
      <header className="border-b border-borda bg-superficie px-6 py-4">
        <h1 className="text-xl font-semibold tracking-tight text-marca-950">Empresa</h1>
        <p className="text-slate-500">O estabelecimento do escritório, como sai na nota e no boleto.</p>
      </header>

      <div className="flex flex-1 flex-col gap-6 p-6">
        {empresas.isPending && <p className="text-slate-500">Carregando a empresa…</p>}

        {empresas.isError && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            {empresas.error.message} Verifique se a API está no ar e tente de novo.
          </p>
        )}

        {empresa && (
          <form
            className="flex max-w-xl flex-col gap-5 rounded-[--radius-cartao] border border-borda bg-superficie p-5"
            onSubmit={(evento) => {
              evento.preventDefault();
              salvar.mutate();
            }}
          >
            <div>
              <h2 className="font-semibold text-marca-950">Dados do estabelecimento</h2>
              <p className="text-slate-500">
                Foram gravados uma vez, na instalação, a partir de configuração. Se algum dígito
                entrou errado lá, é aqui que se conserta.
              </p>
            </div>

            {salva && (
              <p
                role="status"
                className="rounded-[--radius-controle] bg-emerald-50 px-3 py-2 text-emerald-800"
              >
                Dados gravados.
              </p>
            )}

            <Entrada
              rotulo="Razão social"
              required
              value={valores.razaoSocial}
              onChange={(evento) => alterar("razaoSocial", evento.target.value)}
              erro={erroDe("razaoSocial")}
              ajuda={sugestaoDe("razaoSocial") ?? "Como consta no cartão CNPJ."}
            />

            <Entrada
              rotulo="Nome fantasia"
              value={valores.nomeFantasia}
              onChange={(evento) => alterar("nomeFantasia", evento.target.value)}
            />

            <EntradaMascarada
              rotulo="CNPJ"
              inputMode="numeric"
              mascara={mascararCnpj}
              digitos={valores.cnpj}
              aoMudar={(valor) => alterar("cnpj", valor)}
              erro={erroDe("cnpj")}
              ajuda={sugestaoDe("cnpj")}
            />

            <Botao type="submit" disabled={salvar.isPending}>
              {salvar.isPending ? "Salvando…" : "Salvar"}
            </Botao>
          </form>
        )}

        <p className="max-w-xl text-slate-500">
          Endereço e contato do estabelecimento ainda não existem aqui. Vão entrar junto com a
          NFS-e, que é o que passa a exigir os dois.
        </p>
      </div>
    </>
  );
}
