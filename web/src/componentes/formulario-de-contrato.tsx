"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import { AreaDeTexto, Botao, Entrada, EntradaMascarada, Selecao } from "@/componentes/controles";
import type { components } from "@/api/esquema";
import { digitosDoValor, formatarValor, mascararDinheiro, valorDosDigitos } from "@/lib/dinheiro";

type DadosDeContrato = components["schemas"]["DadosDeContrato"];
type Problema = components["schemas"]["Problema"];

function hojeIso(): string {
  return new Date().toISOString().slice(0, 10);
}

const vazio: DadosDeContrato = {
  clienteId: "",
  descricao: "Honorários contábeis",
  valor: 0,
  diaDeVencimento: 10,
  inicioDaVigencia: hojeIso(),
  fimDaVigencia: null,
  situacao: "Ativo",
  observacoes: "",
};

function problemasDaResposta(erro: unknown): Problema[] {
  if (erro && typeof erro === "object" && "problemas" in erro && Array.isArray(erro.problemas)) {
    return erro.problemas as Problema[];
  }
  return [];
}

export function FormularioDeContrato({ id }: { id?: string }) {
  const navegacao = useRouter();
  const clienteDeConsultas = useQueryClient();
  const editando = Boolean(id);

  const [dados, definirDados] = useState<DadosDeContrato>(vazio);
  const [problemas, definirProblemas] = useState<Problema[]>([]);

  /*
   * O valor guarda dígitos, e a máscara os mostra em reais. Entram pelos
   * centavos: 1 vira 0,01, depois 0,12, depois 1,23. Assim a vírgula nunca é
   * digitada, e não sobra ambiguidade nenhuma para interpretar.
   */
  const [valorEmDigitos, definirDigitos] = useState("");

  const clientes = useQuery({
    queryKey: ["clientes"],
    queryFn: async () => {
      const { data, error } = await api.GET("/clientes");
      if (error || !data) throw new Error("Não foi possível carregar os clientes.");
      return data;
    },
  });

  const existente = useQuery({
    queryKey: ["contrato", id],
    enabled: editando,
    queryFn: async () => {
      const { data, error } = await api.GET("/contratos/{id}", { params: { path: { id: id! } } });
      if (error || !data) throw new Error("Este contrato não foi encontrado.");
      return data;
    },
  });

  useEffect(() => {
    if (!existente.data) return;
    const { id: _id, codigo: _codigo, ...resto } = existente.data;
    definirDados(resto);
    definirDigitos(digitosDoValor(resto.valor));
  }, [existente.data]);

  const salvar = useMutation({
    mutationFn: async (corpo: DadosDeContrato) => {
      const resposta = editando
        ? await api.PUT("/contratos/{id}", { params: { path: { id: id! } }, body: corpo })
        : await api.POST("/contratos", { body: corpo });

      if (resposta.error) throw resposta.error;
      return resposta.data;
    },
    onSuccess: () => {
      definirProblemas([]);
      clienteDeConsultas.invalidateQueries({ queryKey: ["contratos"] });
      navegacao.push("/contratos");
    },
    onError: (erro) => definirProblemas(problemasDaResposta(erro)),
  });

  const erroDe = (campo: string) => problemas.find((p) => p.campo === campo)?.descricao;
  const sugestaoDe = (campo: string) => problemas.find((p) => p.campo === campo)?.sugestao;

  function alterar<C extends keyof DadosDeContrato>(campo: C, valor: DadosDeContrato[C]) {
    definirDados((atual) => ({ ...atual, [campo]: valor }));
  }

  if (editando && existente.isPending) {
    return <p className="p-6 text-slate-500">Carregando o contrato…</p>;
  }

  if (clientes.data && clientes.data.length === 0) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-3 p-6 text-center">
        <p className="font-medium text-slate-700">Não há clientes ainda.</p>
        <p className="max-w-md text-slate-500">
          Um contrato é o acordo com um cliente. Cadastre a pessoa e depois marque-a como cliente
          do escritório.
        </p>
        <Link href="/clientes" className="font-semibold text-marca-700 hover:underline">
          Ir para Clientes
        </Link>
      </div>
    );
  }

  return (
    <form
      className="flex flex-1 flex-col"
      onSubmit={(evento) => {
        evento.preventDefault();
        salvar.mutate({ ...dados, valor: valorDosDigitos(valorEmDigitos) });
      }}
    >
      <header className="border-b border-borda bg-superficie px-6 py-4">
        <h1 className="text-xl font-semibold tracking-tight text-marca-950">
          {editando ? `Contrato ${existente.data?.codigo ?? ""}` : "Novo contrato"}
        </h1>
        <p className="text-slate-500">
          O dia do vencimento vale para todos os meses. Em meses mais curtos, cai no último dia.
        </p>
      </header>

      <div className="flex flex-1 flex-col gap-6 p-6">
        {problemas.length > 0 && (
          <div role="alert" className="rounded-[--radius-controle] border border-red-200 bg-red-50 px-4 py-3">
            <p className="font-semibold text-red-800">
              {problemas.length === 1
                ? "Há um problema no contrato."
                : `Há ${problemas.length} problemas no contrato.`}
            </p>
            <p className="mt-1 text-red-700">Os campos com erro estão marcados abaixo.</p>
          </div>
        )}

        <section className="grid gap-4 rounded-[--radius-cartao] border border-borda bg-superficie p-5 sm:grid-cols-2 lg:grid-cols-4">
          <div className="sm:col-span-2">
            <Selecao
              rotulo="Cliente"
              value={dados.clienteId}
              onChange={(evento) => alterar("clienteId", evento.target.value)}
              erro={erroDe("clienteId")}
              ajuda={sugestaoDe("clienteId")}
            >
              <option value="">Escolha um cliente…</option>
              {clientes.data?.map((cliente) => (
                <option key={cliente.id} value={cliente.id}>
                  {cliente.codigo} — {cliente.nomeFantasia || cliente.nome}
                </option>
              ))}
            </Selecao>
          </div>

          <div className="sm:col-span-2">
            <Entrada
              rotulo="Descrição"
              value={dados.descricao ?? ""}
              onChange={(evento) => alterar("descricao", evento.target.value)}
              erro={erroDe("descricao")}
              ajuda={sugestaoDe("descricao") ?? "O que está sendo cobrado."}
            />
          </div>

          <EntradaMascarada
            rotulo="Valor mensal"
            className="text-right"
            digitos={valorEmDigitos}
            mascara={mascararDinheiro}
            aoMudar={definirDigitos}
            erro={erroDe("valor")}
            ajuda={
              sugestaoDe("valor") ??
              (valorEmDigitos
                ? formatarValor(valorDosDigitos(valorEmDigitos))
                : "Digite só os números: os centavos entram primeiro.")
            }
          />

          <Entrada
            rotulo="Dia do vencimento"
            type="number"
            min={1}
            max={31}
            className="numeros-tabulares text-right"
            value={dados.diaDeVencimento}
            onChange={(evento) => alterar("diaDeVencimento", Number(evento.target.value))}
            erro={erroDe("diaDeVencimento")}
            ajuda={sugestaoDe("diaDeVencimento")}
          />

          <Entrada
            rotulo="Início da vigência"
            type="date"
            value={dados.inicioDaVigencia}
            onChange={(evento) => alterar("inicioDaVigencia", evento.target.value)}
          />

          <Entrada
            rotulo="Fim da vigência"
            type="date"
            value={dados.fimDaVigencia ?? ""}
            onChange={(evento) => alterar("fimDaVigencia", evento.target.value || null)}
            erro={erroDe("fimDaVigencia")}
            ajuda={sugestaoDe("fimDaVigencia") ?? "Em branco: sem prazo."}
          />

          <Selecao
            rotulo="Situação"
            value={dados.situacao}
            onChange={(evento) =>
              alterar("situacao", evento.target.value as DadosDeContrato["situacao"])
            }
            ajuda="Só contratos ativos geram mensalidade."
          >
            <option value="Ativo">Ativo</option>
            <option value="Suspenso">Suspenso</option>
            <option value="Encerrado">Encerrado</option>
          </Selecao>

          <div className="sm:col-span-2 lg:col-span-4">
            <AreaDeTexto
              rotulo="Observações"
              value={dados.observacoes ?? ""}
              onChange={(evento) => alterar("observacoes", evento.target.value)}
            />
          </div>
        </section>
      </div>

      <footer className="sticky bottom-0 flex items-center justify-end gap-3 border-t border-borda bg-superficie px-6 py-3">
        <Link
          href="/contratos"
          className="rounded-[--radius-controle] border border-borda-forte px-4 py-2 font-semibold text-slate-700 transition-colors hover:bg-slate-50"
        >
          Cancelar
        </Link>
        <Botao type="submit" disabled={salvar.isPending}>
          {salvar.isPending ? "Salvando…" : "Salvar"}
        </Botao>
      </footer>
    </form>
  );
}
