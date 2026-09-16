"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import { AreaDeTexto, Botao, Entrada, EntradaMascarada, Selecao } from "@/componentes/controles";
import type { components } from "@/api/esquema";
import { problemasDaResposta } from "@/lib/problemas";
import { digitosDoValor, formatarCompetencia, formatarValor, hojeIso, mascararDinheiro, valorDosDigitos } from "@/lib/dinheiro";
import { useRetornoDaListagem } from "@/lib/estado-na-url";

type DadosDeContrato = components["schemas"]["DadosDeContrato"];
type ContratoDetalhado = components["schemas"]["ContratoDetalhado"];
type Problema = components["schemas"]["Problema"];

const vazio: DadosDeContrato = {
  pessoaId: "",
  descricao: "Honorários contábeis",
  valor: 0,
  diaDeVencimento: 10,
  inicioDaVigencia: hojeIso(),
  fimDaVigencia: null,
  situacao: "Ativo",
  observacoes: "",
};

/* O corpo que a API aceita é o contrato sem o que ela mesma gera. */
function semIdentificadores(contrato: ContratoDetalhado): DadosDeContrato {
  return {
    pessoaId: contrato.pessoaId,
    descricao: contrato.descricao,
    valor: contrato.valor,
    diaDeVencimento: contrato.diaDeVencimento,
    inicioDaVigencia: contrato.inicioDaVigencia,
    fimDaVigencia: contrato.fimDaVigencia ?? null,
    situacao: contrato.situacao,
    observacoes: contrato.observacoes,
  };
}

export function FormularioDeContrato({ id }: { id?: string }) {
  const navegacao = useRouter();
  const clienteDeConsultas = useQueryClient();
  const editando = Boolean(id);

  /* Sair daqui devolve a listagem como ela estava, e não a listagem do zero. */
  const listagem = useRetornoDaListagem("/contratos");

  const [problemas, definirProblemas] = useState<Problema[]>([]);

  /*
   * Nulos até alguém mexer. Enquanto forem nulos, o formulário mostra o que
   * veio da consulta; a partir da primeira tecla, mostra o rascunho.
   *
   * O caminho óbvio seria um efeito copiando a consulta para o estado quando
   * ela chega. Além de o React desaconselhar, ele passa por cima do que a
   * pessoa está preenchendo toda vez que o dado chega de novo.
   */
  const [rascunho, definirRascunho] = useState<DadosDeContrato | null>(null);

  /*
   * O valor guarda dígitos, e a máscara os mostra em reais. Entram pelos
   * centavos: 1 vira 0,01, depois 0,12, depois 1,23. Assim a vírgula nunca é
   * digitada, e não sobra ambiguidade nenhuma para interpretar.
   */
  const [digitados, definirDigitos] = useState<string | null>(null);

  const clientes = useQuery({
    queryKey: ["pessoas", "clientes"],
    queryFn: async () => {
      const { data, error } = await api.GET("/pessoas", {
        params: { query: { papel: "Cliente", tamanho: 200 } },
      });
      if (error || !data) throw new Error("Não foi possível carregar os clientes.");
      return data.itens;
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

  /* O histórico explica o valor de hoje, e é onde o reajuste do ano passado aparece. */
  const valores = useQuery({
    queryKey: ["contrato-valores", id],
    enabled: editando,
    queryFn: async () => {
      const { data, error } = await api.GET("/contratos/{id}/valores", { params: { path: { id: id! } } });
      if (error || !data) throw new Error("Não foi possível carregar o histórico de valores.");
      return data;
    },
  });

  const carregado = existente.data;
  const dados = rascunho ?? (carregado ? semIdentificadores(carregado) : vazio);
  const valorEmDigitos = digitados ?? (carregado ? digitosDoValor(carregado.valor) : "");

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
      navegacao.push(listagem);
    },
    onError: (erro) => definirProblemas(problemasDaResposta(erro)),
  });

  const erroDe = (campo: string) => problemas.find((p) => p.campo === campo)?.descricao;
  const sugestaoDe = (campo: string) => problemas.find((p) => p.campo === campo)?.sugestao;

  function alterar<C extends keyof DadosDeContrato>(campo: C, valor: DadosDeContrato[C]) {
    definirRascunho({ ...dados, [campo]: valor });
  }

  if (editando && existente.isPending) {
    return <p className="p-6 text-slate-600">Carregando o contrato…</p>;
  }

  if (clientes.data && clientes.data.length === 0) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-3 p-6 text-center">
        <p className="font-medium text-slate-700">Não há clientes ainda.</p>
        <p className="max-w-md text-slate-600">
          Um contrato é o acordo com um cliente. Cadastre a pessoa e marque nela o papel
          Cliente.
        </p>
        <Link href="/pessoas" className="font-semibold text-marca-700 hover:underline">
          Ir para Pessoas
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
        <p className="text-slate-600">
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
              value={dados.pessoaId}
              onChange={(evento) => alterar("pessoaId", evento.target.value)}
              erro={erroDe("pessoaId")}
              ajuda={sugestaoDe("pessoaId")}
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
              (editando
                ? "Corrige o valor em vigor. Para mudar a partir de um mês, use Reajustar na listagem."
                : valorEmDigitos
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

        {editando && valores.data && valores.data.length > 0 && (
          <section aria-labelledby="titulo-do-historico" className="flex flex-col gap-2">
            <h2 id="titulo-do-historico" className="font-semibold text-marca-950">
              Histórico de valores
            </h2>
            <p className="text-slate-600">
              A mensalidade de cada competência sai pelo valor que valia nela. Reajustar abre uma vigência
              nova, e fica na listagem de contratos.
            </p>

            <ul className="rounded-[--radius-cartao] border border-borda bg-superficie px-4">
              {valores.data.map((valor) => (
                <li
                  key={valor.id}
                  className="flex flex-wrap items-baseline justify-between gap-2 border-b border-borda py-2 last:border-0"
                >
                  <span className="text-slate-700">
                    A partir de {formatarCompetencia(valor.vigenteDeAno, valor.vigenteDeMes)}
                    <span className="block text-sm text-slate-500">
                      {valor.motivo}
                      {valor.percentual != null && ` · ${String(valor.percentual).replace(".", ",")}%`}
                    </span>
                  </span>
                  <span className="numeros-tabulares font-medium text-slate-800">
                    {formatarValor(valor.valor)}
                  </span>
                </li>
              ))}
            </ul>
          </section>
        )}
      </div>

      <footer className="sticky bottom-0 flex items-center justify-end gap-3 border-t border-borda bg-superficie px-6 py-3">
        <Link
          href={listagem}
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
