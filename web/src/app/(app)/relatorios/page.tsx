"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { Botao, Entrada, Selecao } from "@/componentes/controles";
import { formatarValor } from "@/lib/dinheiro";
import { useConsultaDaUrl } from "@/lib/estado-na-url";

import { useCentrosDeCusto } from "../lancamentos/classificacao";

type RelatorioDeCategorias = components["schemas"]["RelatorioDeCategorias"];
type GrupoDoRelatorio = components["schemas"]["GrupoDoRelatorio"];
type RegimeDoRelatorio = components["schemas"]["RegimeDoRelatorio"];
type Problema = components["schemas"]["Problema"];

const MESES_PADRAO = 6;

/** A competência de hoje, como o campo de mês a escreve: 2026-03. */
function competenciaDeHoje(): string {
  const agora = new Date();
  return `${agora.getFullYear()}-${String(agora.getMonth() + 1).padStart(2, "0")}`;
}

/** Soma meses a uma competência, sem passar por fuso horário. */
function somarMeses(competencia: string, meses: number): string {
  const [ano, mes] = competencia.split("-").map(Number);
  if (!ano || !mes) return competenciaDeHoje();

  const total = ano * 12 + (mes - 1) + meses;
  return `${Math.floor(total / 12)}-${String((total % 12) + 1).padStart(2, "0")}`;
}

function rotuloDoMes(mes: { ano: number; mes: number }): string {
  return `${String(mes.mes).padStart(2, "0")}/${mes.ano}`;
}

/** Zero vira traço: num fechamento de seis meses, a maioria das células não tem nada. */
function valorOuTraco(valor: number): string {
  return valor === 0 ? "—" : formatarValor(valor);
}

/** Uma planilha lê ponto e vírgula, vírgula decimal e o BOM que diz que o texto é UTF-8. */
function exportar(relatorio: RelatorioDeCategorias, nome: string) {
  const numero = (valor: number) => valor.toFixed(2).replace(".", ",");
  const celula = (texto: string) => `"${texto.split('"').join('""')}"`;

  const linhas: string[][] = [["Categoria", ...relatorio.meses.map(rotuloDoMes), "Total"]];

  for (const grupo of relatorio.grupos) {
    linhas.push([grupo.natureza === "Receber" ? "Receitas" : "Despesas"]);

    for (const linha of grupo.linhas) {
      linhas.push([
        "  ".repeat(linha.nivel - 1) + linha.categoria,
        ...linha.valores.map(numero),
        numero(linha.total),
      ]);
    }

    linhas.push(["Total", ...grupo.totais.map(numero), numero(grupo.total)]);
  }

  linhas.push(["Resultado", ...relatorio.resultado.map(numero), numero(relatorio.resultadoTotal)]);

  const texto = "﻿" + linhas.map((linha) => linha.map(celula).join(";")).join("\r\n");
  const endereco = URL.createObjectURL(new Blob([texto], { type: "text/csv;charset=utf-8" }));

  const link = document.createElement("a");
  link.href = endereco;
  link.download = nome;
  link.click();
  URL.revokeObjectURL(endereco);
}

/**
 * O fechamento por categoria, mês a mês.
 *
 * <p>
 * <b>A tela diz de que regime está falando.</b> Competência responde de que mês
 * é o serviço; caixa, em que mês o dinheiro se moveu. O mesmo honorário aparece
 * em meses diferentes nos dois, e um relatório que não dissesse qual está
 * mostrando seria um número sem pergunta.
 * </p>
 * <p>
 * <b>Meses em colunas.</b> A comparação entre meses é a pergunta que um
 * fechamento responde, e ela só existe se os meses estiverem lado a lado. A
 * tabela rola na horizontal no celular, em vez de virar cartão: cartão por
 * categoria desfaz justamente a comparação.
 * </p>
 */
export default function Relatorios() {
  const { ler, gravar, estabilizada } = useConsultaDaUrl("/relatorios");

  const ate = ler("ate") ?? competenciaDeHoje();
  const de = ler("de") ?? somarMeses(ate, -(MESES_PADRAO - 1));
  const regime: RegimeDoRelatorio = ler("regime") === "Caixa" ? "Caixa" : "Competencia";
  const centro = ler("centro") ?? "";

  const centros = useCentrosDeCusto().data ?? [];

  const [deAno, deMes] = de.split("-").map(Number);
  const [ateAno, ateMes] = ate.split("-").map(Number);

  const relatorio = useQuery({
    queryKey: ["relatorio-por-categoria", regime, de, ate, centro],
    queryFn: async () => {
      const { data, error } = await api.GET("/relatorios/por-categoria", {
        params: {
          query: {
            regime,
            deAno,
            deMes,
            ateAno,
            ateMes,
            ...(centro === "sem" ? { semCentroDeCusto: true } : centro ? { centroDeCustoId: centro } : {}),
          },
        },
      });

      if (error) {
        const problemas = (error as { problemas?: Problema[] }).problemas ?? [];
        throw new Error(
          problemas.length > 0
            ? `${problemas[0].descricao} ${problemas[0].sugestao}`
            : "Não foi possível montar o relatório.",
        );
      }

      return data!;
    },
    enabled: estabilizada,
    placeholderData: keepPreviousData,
  });

  const dados = relatorio.data;
  const vazio = dados !== undefined && dados.grupos.every((grupo) => grupo.linhas.length === 0);

  function tabela(grupo: GrupoDoRelatorio) {
    const titulo = grupo.natureza === "Receber" ? "Receitas" : "Despesas";

    return (
      <section key={grupo.natureza} aria-labelledby={`titulo-${grupo.natureza}`} className="flex flex-col gap-2">
        <h2 id={`titulo-${grupo.natureza}`} className="text-lg font-semibold text-marca-950">
          {titulo}
        </h2>

        {grupo.linhas.length === 0 ? (
          <p className="rounded-[--radius-cartao] border border-dashed border-borda-forte bg-superficie px-4 py-6 text-center text-slate-600">
            Nada em {titulo.toLowerCase()} neste período.
          </p>
        ) : (
          <div className="overflow-x-auto rounded-[--radius-cartao] border border-borda bg-superficie shadow-nivel-1">
            <table className="w-full min-w-3xl border-collapse text-sm">
              <thead>
                <tr className="border-b border-borda text-xs tracking-wide text-slate-500 uppercase">
                  <th scope="col" className="px-4 py-3 text-left font-semibold">
                    Categoria
                  </th>
                  {dados!.meses.map((mes) => (
                    <th key={rotuloDoMes(mes)} scope="col" className="px-4 py-3 text-right font-semibold">
                      {rotuloDoMes(mes)}
                    </th>
                  ))}
                  <th scope="col" className="px-4 py-3 text-right font-semibold">
                    Total
                  </th>
                </tr>
              </thead>

              <tbody>
                {grupo.linhas.map((linha) => (
                  <tr key={linha.categoriaId ?? "sem-categoria"} className="border-b border-borda last:border-0">
                    <th
                      scope="row"
                      className={
                        "px-4 py-2 text-left font-normal " +
                        (linha.nivel === 1 ? "font-medium text-slate-800" : "text-slate-700")
                      }
                      /* O recuo mostra o nível, como no plano de contas. */
                      style={{ paddingLeft: `${1 + (linha.nivel - 1) * 1.25}rem` }}
                    >
                      {linha.categoria}
                    </th>
                    {linha.valores.map((valor, coluna) => (
                      <td key={coluna} className="numeros-tabulares px-4 py-2 text-right text-slate-700">
                        {valorOuTraco(valor)}
                      </td>
                    ))}
                    <td className="numeros-tabulares px-4 py-2 text-right font-medium text-slate-800">
                      {formatarValor(linha.total)}
                    </td>
                  </tr>
                ))}
              </tbody>

              <tfoot>
                <tr className="border-t border-borda-forte bg-slate-50">
                  <th scope="row" className="px-4 py-3 text-left font-semibold text-slate-800">
                    Total
                  </th>
                  {grupo.totais.map((valor, coluna) => (
                    <td key={coluna} className="numeros-tabulares px-4 py-3 text-right font-semibold text-slate-800">
                      {valorOuTraco(valor)}
                    </td>
                  ))}
                  <td className="numeros-tabulares px-4 py-3 text-right font-semibold text-slate-800">
                    {formatarValor(grupo.total)}
                  </td>
                </tr>
              </tfoot>
            </table>
          </div>
        )}
      </section>
    );
  }

  return (
    <>
      <header className="flex flex-wrap items-center justify-between gap-4 border-b border-borda bg-superficie px-6 py-4">
        <div>
          <h1 id="conteudo" tabIndex={-1} className="text-xl font-semibold tracking-tight text-marca-950">
            Relatórios
          </h1>
          <p className="text-slate-600">
            O fechamento por categoria, mês a mês, com o resultado de cada um.
          </p>
        </div>

        <Botao
          aparencia="secundario"
          type="button"
          disabled={!dados || vazio}
          onClick={() => dados && exportar(dados, `relatorio-${regime.toLowerCase()}-${de}-a-${ate}.csv`)}
        >
          Exportar CSV
        </Botao>
      </header>

      <div className="flex flex-1 flex-col gap-6 p-6">
        <section
          aria-label="Recorte"
          className="flex flex-wrap items-end gap-3 rounded-[--radius-cartao] border border-borda bg-superficie p-4 shadow-nivel-1"
        >
          <div className="w-full sm:w-44">
            <Selecao
              rotulo="Regime"
              value={regime}
              onChange={(evento) => gravar({ regime: evento.target.value === "Caixa" ? "Caixa" : null })}
              ajuda={
                regime === "Caixa"
                  ? "O mês em que o dinheiro se moveu."
                  : "O mês a que o lançamento se refere."
              }
            >
              <option value="Competencia">Competência</option>
              <option value="Caixa">Caixa</option>
            </Selecao>
          </div>

          <div className="w-full sm:w-40">
            <Entrada
              rotulo="De"
              type="month"
              placeholder="AAAA-MM"
              className="numeros-tabulares"
              value={de}
              onChange={(evento) => gravar({ de: evento.target.value })}
            />
          </div>

          <div className="w-full sm:w-40">
            <Entrada
              rotulo="Até"
              type="month"
              placeholder="AAAA-MM"
              className="numeros-tabulares"
              value={ate}
              onChange={(evento) => gravar({ ate: evento.target.value })}
            />
          </div>

          {centros.length > 0 && (
            <div className="w-full sm:w-52">
              <Selecao
                rotulo="Centro de custo"
                value={centro}
                onChange={(evento) => gravar({ centro: evento.target.value })}
              >
                <option value="">Todos</option>
                <option value="sem">Sem centro de custo</option>
                {centros.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.nome}
                  </option>
                ))}
              </Selecao>
            </div>
          )}
        </section>

        {relatorio.isPending && <p className="text-slate-600">Montando o relatório…</p>}

        {relatorio.isError && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            {relatorio.error.message}
          </p>
        )}

        {vazio && (
          <p className="rounded-[--radius-cartao] border border-dashed border-borda-forte bg-superficie px-6 py-10 text-center text-slate-600">
            Nenhum lançamento no período. Cancelados e renegociados não entram, e movimentos de conta — tarifa,
            rendimento, aporte — também não: eles não têm categoria.
          </p>
        )}

        {dados && !vazio && (
          <>
            {dados.grupos.map(tabela)}

            <section aria-labelledby="titulo-do-resultado" className="flex flex-col gap-2">
              <h2 id="titulo-do-resultado" className="text-lg font-semibold text-marca-950">
                Resultado
              </h2>
              <p className="text-slate-600">
                Receitas menos despesas,{" "}
                {regime === "Caixa" ? "pelo mês em que o dinheiro se moveu" : "pelo mês a que os lançamentos se referem"}
                .
              </p>

              <div className="overflow-x-auto rounded-[--radius-cartao] border border-borda bg-superficie shadow-nivel-1">
                <table className="w-full min-w-3xl border-collapse text-sm">
                  <thead>
                    <tr className="border-b border-borda text-xs tracking-wide text-slate-500 uppercase">
                      <th scope="col" className="px-4 py-3 text-left font-semibold">
                        Período
                      </th>
                      {dados.meses.map((mes) => (
                        <th key={rotuloDoMes(mes)} scope="col" className="px-4 py-3 text-right font-semibold">
                          {rotuloDoMes(mes)}
                        </th>
                      ))}
                      <th scope="col" className="px-4 py-3 text-right font-semibold">
                        Total
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr>
                      <th scope="row" className="px-4 py-3 text-left font-semibold text-slate-800">
                        Resultado
                      </th>
                      {dados.resultado.map((valor, coluna) => (
                        <td
                          key={coluna}
                          className={
                            "numeros-tabulares px-4 py-3 text-right font-semibold " +
                            (valor < 0 ? "text-red-700" : "text-slate-800")
                          }
                        >
                          {valorOuTraco(valor)}
                        </td>
                      ))}
                      <td
                        className={
                          "numeros-tabulares px-4 py-3 text-right font-semibold " +
                          (dados.resultadoTotal < 0 ? "text-red-700" : "text-slate-800")
                        }
                      >
                        {formatarValor(dados.resultadoTotal)}
                      </td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </section>
          </>
        )}
      </div>
    </>
  );
}
