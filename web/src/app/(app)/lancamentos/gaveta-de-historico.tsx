"use client";

import { useQuery } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import { Gaveta } from "@/componentes/gaveta";
import { formatarData, formatarValor } from "@/lib/dinheiro";

import { origens } from "../contas-bancarias/[id]/origens";
import { rotulosDeTipo } from "../contas-bancarias/gaveta-de-conta";
import { useCategorias, useCentrosDeCusto } from "./classificacao";
import { useContasBancarias } from "./contas";

/** De quem é o histórico: um lançamento ou uma conta. Nulo com a gaveta fechada. */
export type AlvoDoHistorico = { tipo: "lancamento"; id: string } | { tipo: "conta"; id: string };

type Props = {
  alvo: AlvoDoHistorico | null;
  titulo: string;
  descricao?: string;
  aoFechar: () => void;
};

type Formato = "texto" | "valor" | "data" | "sim-nao" | "categoria" | "centro" | "conta" | "rotulo";

/*
 * Os campos que a tela sabe contar, na ordem em que aparecem. O resto fica
 * guardado na trilha e não se mostra: o id da pessoa ou do parcelamento não diz
 * nada a quem lê, e o que ele diria já está na descrição do lançamento.
 */
const CAMPOS: [campo: string, rotulo: string, formato: Formato][] = [
  ["situacao", "Situação", "rotulo"],
  ["descricao", "Descrição", "texto"],
  ["valor", "Valor", "valor"],
  ["vencimento", "Vencimento", "data"],
  ["valorPago", "Valor da baixa", "valor"],
  ["juros", "Juros", "valor"],
  ["multa", "Multa", "valor"],
  ["desconto", "Desconto", "valor"],
  ["pagoEm", "Data da baixa", "data"],
  ["origemDaBaixa", "Como foi baixado", "rotulo"],
  ["motivoDoCancelamento", "Motivo do cancelamento", "texto"],
  ["categoriaId", "Categoria", "categoria"],
  ["centroDeCustoId", "Centro de custo", "centro"],
  ["contaId", "Conta", "conta"],
  ["data", "Data", "data"],
  ["origem", "Origem", "rotulo"],
  ["nome", "Nome", "texto"],
  ["tipo", "Tipo", "rotulo"],
  ["banco", "Banco", "texto"],
  ["agencia", "Agência", "texto"],
  ["numero", "Número", "texto"],
  ["saldoInicial", "Saldo inicial", "valor"],
  ["saldoInicialEm", "Saldo inicial em", "data"],
  ["ativa", "Ativa", "sim-nao"],
  ["recebeCobrancas", "Recebe as cobranças do PSP", "sim-nao"],
];

/* Os valores que a trilha guarda pelo nome, lidos como nas outras telas. */
const rotulos: Record<string, string> = {
  ...origens,
  ...rotulosDeTipo,
  Aberto: "Em aberto",
  Pago: "Baixado",
  Cancelado: "Cancelado",
  Renegociado: "Renegociado",
  manual: "À mão",
  lote: "Em lote",
  cobranca: "Pelo PSP",
};

const momento = new Intl.DateTimeFormat("pt-BR", { dateStyle: "short", timeStyle: "short" });

/**
 * Quem fez o quê, e quando, num lançamento ou numa conta.
 *
 * <p>
 * <b>Só leitura.</b> A trilha não se corrige: o que está errado se desfaz com
 * a própria operação, como o estorno, e o desfazer entra no histórico também.
 * </p>
 * <p>
 * A trilha começou a ser escrita com esta tela. Um lançamento mais antigo não
 * tem o próprio começo nela, e a gaveta diz isso em vez de deixar parecer que
 * nada aconteceu.
 * </p>
 */
export function GavetaDeHistorico({ alvo, titulo, descricao, aoFechar }: Props) {
  const categorias = useCategorias().data;
  const centros = useCentrosDeCusto().data;
  const contas = useContasBancarias().data;

  const historico = useQuery({
    queryKey: ["historico", alvo?.tipo, alvo?.id],
    enabled: alvo !== null,
    /* Cada abertura lê de novo: quem abre o histórico costuma ter acabado de mexer. */
    staleTime: 0,
    queryFn: async () => {
      const { data, error } =
        alvo?.tipo === "conta"
          ? await api.GET("/contas-bancarias/{id}/historico", { params: { path: { id: alvo.id } } })
          : await api.GET("/lancamentos/{id}/historico", { params: { path: { id: alvo!.id } } });
      if (error || !data) throw new Error("Não foi possível carregar o histórico.");
      return data;
    },
  });

  function formatar(valor: string | null | undefined, formato: Formato): string {
    if (valor === null || valor === undefined) return "—";

    switch (formato) {
      case "valor":
        return formatarValor(Number(valor));
      case "data":
        return formatarData(valor);
      case "sim-nao":
        return valor === "true" ? "Sim" : "Não";
      case "categoria":
        return categorias?.find((item) => item.id === valor)?.caminho ?? "—";
      case "centro":
        return centros?.find((item) => item.id === valor)?.nome ?? "—";
      case "conta":
        return contas?.find((item) => item.id === valor)?.nome ?? "—";
      case "rotulo":
        return rotulos[valor] ?? valor;
      default:
        return valor;
    }
  }

  const eventos = historico.data;
  const entidadeDoAlvo = alvo?.tipo === "conta" ? "ContaBancaria" : "Lancamento";
  const semOComeco =
    eventos !== undefined &&
    (eventos.length === 0 || eventos[0].entidade !== entidadeDoAlvo || eventos[0].acao !== "Criado");

  return (
    <Gaveta titulo={titulo} descricao={descricao} aberta={alvo !== null} aoFechar={aoFechar}>
      {historico.isPending && <p className="text-slate-600">Carregando o histórico…</p>}

      {historico.isError && (
        <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
          Não foi possível carregar o histórico. Feche e abra de novo em instantes.
        </p>
      )}

      {semOComeco && (
        <p className="mb-5 rounded-[--radius-controle] bg-slate-100 px-4 py-3 text-slate-700">
          A trilha começou a ser escrita depois que{" "}
          {alvo?.tipo === "conta" ? "esta conta foi cadastrada" : "este lançamento foi criado"}: o que aconteceu
          antes disso não aparece aqui.
        </p>
      )}

      {eventos && eventos.length > 0 && (
        <ol className="flex flex-col gap-5 border-l border-borda pl-5">
          {eventos.map((evento) => {
            const linhas = CAMPOS.flatMap(([campo, rotulo, formato]) => {
              const mudanca = evento.mudancas.find((item) => item.campo === campo);
              return mudanca ? [{ campo, rotulo, formato, mudanca }] : [];
            });

            return (
              <li key={evento.id} className="relative">
                <span
                  aria-hidden="true"
                  className="absolute top-1.5 -left-[25.5px] size-2.5 rounded-full bg-marca-600 ring-4 ring-superficie"
                />
                <p className="font-semibold text-marca-950">{evento.resumo}</p>
                <p className="text-sm text-slate-600">
                  {evento.autor} · <time dateTime={evento.em}>{momento.format(new Date(evento.em))}</time>
                </p>

                {linhas.length > 0 && (
                  <dl className="mt-2 grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-1 text-sm">
                    {linhas.map(({ campo, rotulo, formato, mudanca }) => (
                      <div key={campo} className="contents">
                        <dt className="text-slate-600">{rotulo}</dt>
                        <dd className="break-words text-slate-900 tabular-nums">
                          {evento.acao === "Alterado" ? (
                            <>
                              <span className="text-slate-500">{formatar(mudanca.antes, formato)}</span>
                              <span aria-hidden="true"> → </span>
                              <span className="sr-only"> passou a </span>
                              {formatar(mudanca.depois, formato)}
                            </>
                          ) : (
                            formatar(evento.acao === "Apagado" ? mudanca.antes : mudanca.depois, formato)
                          )}
                        </dd>
                      </div>
                    ))}
                  </dl>
                )}
              </li>
            );
          })}
        </ol>
      )}
    </Gaveta>
  );
}
