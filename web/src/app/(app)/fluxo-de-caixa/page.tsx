"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { Entrada } from "@/componentes/controles";
import { formatarCompetencia, formatarData, formatarValor, hojeIso } from "@/lib/dinheiro";
import { useConsultaDaUrl } from "@/lib/estado-na-url";

type AgrupamentoDoFluxo = components["schemas"]["AgrupamentoDoFluxo"];
type PeriodoDoFluxo = components["schemas"]["PeriodoDoFluxo"];
type Problema = components["schemas"]["Problema"];

/** Soma dias a uma data ISO sem passar por fuso horário. */
function somarDias(iso: string, dias: number): string {
  const [ano, mes, dia] = iso.slice(0, 10).split("-").map(Number);
  return new Date(Date.UTC(ano, mes - 1, dia + dias)).toISOString().slice(0, 10);
}

/** Um valor que é zero vira traço: numa tabela de trinta dias, a maioria dos dias não tem nada. */
function valorOuTraco(valor: number): string {
  return valor === 0 ? "—" : formatarValor(valor);
}

function rotuloDoPeriodo(periodo: PeriodoDoFluxo, agrupamento: AgrupamentoDoFluxo): string {
  if (agrupamento === "Dia") return formatarData(periodo.comeco);

  const [ano, mes] = periodo.comeco.split("-").map(Number);
  return formatarCompetencia(ano, mes);
}

/**
 * O fluxo de caixa: o que entrou e saiu das contas, e o que está para entrar e sair.
 *
 * <p>
 * <b>Realizado e projetado lado a lado, e nunca misturados na mesma coluna.</b>
 * Quem olha o saldo de daqui a duas semanas precisa saber quanto dele é
 * dinheiro que já está na conta e quanto é promessa de cliente.
 * </p>
 * <p>
 * <b>O atraso aparece fora da tabela.</b> A API não o projeta, porque não se sabe
 * quando, nem se, ele entra; a tela mostra o valor por cima, para quem decide
 * cobrar ou pagar, sem inflar o saldo de nenhum dia.
 * </p>
 * <p>
 * Período e agrupamento moram na URL, como o recorte das listagens. Sem nada
 * escolhido, são os próximos trinta dias por dia, e o padrão não vai para o
 * endereço.
 * </p>
 */
export default function FluxoDeCaixa() {
  const { ler, gravar, estabilizada } = useConsultaDaUrl("/fluxo-de-caixa");

  const hoje = hojeIso();
  const de = ler("de") ?? hoje;
  const ate = ler("ate") ?? somarDias(de, 30);
  const agrupamento: AgrupamentoDoFluxo = ler("agrupamento") === "Mes" ? "Mes" : "Dia";

  const fluxo = useQuery({
    queryKey: ["fluxo-de-caixa", de, ate, agrupamento],
    queryFn: async () => {
      const { data, error } = await api.GET("/fluxo-de-caixa", {
        params: { query: { de, ate, agrupamento } },
      });

      if (error) {
        const problemas = (error as { problemas?: Problema[] }).problemas ?? [];
        throw new Error(
          problemas.length > 0
            ? `${problemas[0].descricao} ${problemas[0].sugestao}`
            : "Não foi possível calcular o fluxo de caixa.",
        );
      }

      return data!;
    },
    enabled: estabilizada,
    placeholderData: keepPreviousData,
  });

  const resultado = fluxo.data;
  const realizado = resultado ? resultado.totalDeEntradas - resultado.totalDeSaidas : 0;
  const projetado = resultado ? resultado.totalAReceber - resultado.totalAPagar : 0;
  const temAtraso = resultado && (resultado.emAtrasoAReceber > 0 || resultado.emAtrasoAPagar > 0);

  const ehHoje = (periodo: PeriodoDoFluxo) =>
    resultado !== undefined && periodo.comeco <= resultado.hoje && resultado.hoje <= periodo.termino;

  return (
    <>
      <header className="border-b border-borda bg-superficie px-6 py-4">
        <h1 id="conteudo" tabIndex={-1} className="text-xl font-semibold tracking-tight text-marca-950">
          Fluxo de caixa
        </h1>
        <p className="text-slate-600">
          O que entrou e saiu das contas, e o que está para entrar e sair, com o saldo de cada dia.
        </p>
      </header>

      <div className="flex flex-1 flex-col gap-5 p-6">
        <section
          aria-label="Período"
          className="flex flex-wrap items-end gap-3 rounded-[--radius-cartao] border border-borda bg-superficie p-4 shadow-nivel-1"
        >
          <div className="w-full sm:w-44">
            <Entrada
              rotulo="De"
              type="date"
              value={de}
              onChange={(evento) => gravar({ de: evento.target.value })}
            />
          </div>
          <div className="w-full sm:w-44">
            <Entrada
              rotulo="Até"
              type="date"
              value={ate}
              onChange={(evento) => gravar({ ate: evento.target.value })}
            />
          </div>

          <div className="flex gap-2" role="group" aria-label="Agrupamento">
            {([
              ["Dia", "Por dia"],
              ["Mes", "Por mês"],
            ] as const).map(([valor, rotulo]) => (
              <button
                key={valor}
                type="button"
                onClick={() => gravar({ agrupamento: valor === "Dia" ? null : valor })}
                aria-pressed={agrupamento === valor}
                className={
                  "inline-flex min-h-11 items-center rounded-full px-4 text-sm font-medium transition-colors " +
                  (agrupamento === valor
                    ? "bg-marca-600 text-white"
                    : "border border-borda-forte bg-superficie text-slate-700 hover:bg-slate-50")
                }
              >
                {rotulo}
              </button>
            ))}
          </div>
        </section>

        {fluxo.isPending && <p className="text-slate-600">Calculando o fluxo…</p>}

        {fluxo.isError && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            {fluxo.error.message}
          </p>
        )}

        {resultado && (
          <>
            <div className="grid gap-px overflow-hidden rounded-[--radius-cartao] border border-borda bg-borda sm:grid-cols-2 lg:grid-cols-4">
              {[
                {
                  rotulo: `Saldo em ${formatarData(resultado.de)}`,
                  valor: resultado.saldoNoInicio,
                  detalhe: "no começo do dia, somando as contas",
                },
                {
                  rotulo: "Realizado",
                  valor: realizado,
                  detalhe: `${formatarValor(resultado.totalDeEntradas)} entraram, ${formatarValor(resultado.totalDeSaidas)} saíram`,
                },
                {
                  rotulo: "Projetado",
                  valor: projetado,
                  detalhe: `${formatarValor(resultado.totalAReceber)} a receber, ${formatarValor(resultado.totalAPagar)} a pagar`,
                },
                {
                  rotulo: `Saldo em ${formatarData(resultado.ate)}`,
                  valor: resultado.saldoNoFim,
                  detalhe: "com o realizado e o projetado",
                },
              ].map((cartao) => (
                <div key={cartao.rotulo} className="flex flex-col gap-1 bg-superficie px-5 py-4">
                  <span className="text-xs font-semibold tracking-wide text-slate-500 uppercase">
                    {cartao.rotulo}
                  </span>
                  <span
                    className={
                      "numeros-tabulares text-2xl font-semibold " +
                      (cartao.valor < 0 ? "text-red-700" : "text-slate-800")
                    }
                  >
                    {formatarValor(cartao.valor)}
                  </span>
                  <span className="text-sm text-slate-500">{cartao.detalhe}</span>
                </div>
              ))}
            </div>

            {temAtraso && (
              <p className="rounded-[--radius-controle] border border-amber-300 bg-amber-50 px-4 py-3 text-amber-900">
                Fora da projeção, em atraso: {formatarValor(resultado.emAtrasoAReceber)} a receber e{" "}
                {formatarValor(resultado.emAtrasoAPagar)} a pagar. Não se sabe quando esse dinheiro entra
                ou sai, e por isso ele não conta no saldo de nenhum dia.
              </p>
            )}

            {/* No celular, cartão; da largura média para cima, tabela: são seis colunas de dinheiro. */}
            <ul className="flex flex-col gap-3 md:hidden">
              {resultado.periodos.map((periodo) => (
                <li
                  key={periodo.comeco}
                  className={
                    "rounded-[--radius-cartao] border p-4 shadow-nivel-1 " +
                    (ehHoje(periodo) ? "border-marca-400 bg-marca-50" : "border-borda bg-superficie")
                  }
                >
                  <div className="flex items-baseline justify-between gap-3">
                    <span className="font-semibold text-slate-800">
                      {rotuloDoPeriodo(periodo, agrupamento)}
                      {ehHoje(periodo) && <span className="ml-2 text-xs font-semibold text-marca-700">hoje</span>}
                    </span>
                    <span
                      className={
                        "numeros-tabulares font-semibold " + (periodo.saldo < 0 ? "text-red-700" : "text-slate-800")
                      }
                    >
                      {formatarValor(periodo.saldo)}
                    </span>
                  </div>

                  <dl className="numeros-tabulares mt-2 grid grid-cols-2 gap-x-3 gap-y-1 text-sm">
                    <dt className="text-slate-500">Entrou</dt>
                    <dd className="text-right text-emerald-700">{valorOuTraco(periodo.entradas)}</dd>
                    <dt className="text-slate-500">Saiu</dt>
                    <dd className="text-right text-red-700">{valorOuTraco(periodo.saidas)}</dd>
                    <dt className="text-slate-500">A receber</dt>
                    <dd className="text-right text-slate-700">{valorOuTraco(periodo.aReceber)}</dd>
                    <dt className="text-slate-500">A pagar</dt>
                    <dd className="text-right text-slate-700">{valorOuTraco(periodo.aPagar)}</dd>
                  </dl>
                </li>
              ))}
            </ul>

            <div className="hidden overflow-x-auto rounded-[--radius-cartao] border border-borda bg-superficie shadow-nivel-1 md:block">
              <table className="w-full min-w-3xl border-collapse text-left">
                <caption className="sr-only">
                  Fluxo de caixa de {formatarData(resultado.de)} a {formatarData(resultado.ate)}
                </caption>
                <thead>
                  <tr className="border-b border-borda bg-slate-50/60 text-xs tracking-wide text-slate-500 uppercase">
                    <th scope="col" className="px-4 py-3 font-semibold">
                      {agrupamento === "Dia" ? "Dia" : "Mês"}
                    </th>
                    <th scope="col" className="px-4 py-3 text-right font-semibold">
                      Entrou
                    </th>
                    <th scope="col" className="px-4 py-3 text-right font-semibold">
                      Saiu
                    </th>
                    <th scope="col" className="px-4 py-3 text-right font-semibold">
                      A receber
                    </th>
                    <th scope="col" className="px-4 py-3 text-right font-semibold">
                      A pagar
                    </th>
                    <th scope="col" className="px-4 py-3 text-right font-semibold">
                      Saldo
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {resultado.periodos.map((periodo) => (
                    <tr
                      key={periodo.comeco}
                      aria-current={ehHoje(periodo) ? "date" : undefined}
                      className={
                        "numeros-tabulares border-b border-borda last:border-0 " +
                        (ehHoje(periodo) ? "bg-marca-50" : "")
                      }
                    >
                      <th scope="row" className="px-4 py-2 text-left font-medium text-slate-800">
                        {rotuloDoPeriodo(periodo, agrupamento)}
                        {ehHoje(periodo) && <span className="ml-2 text-xs font-semibold text-marca-700">hoje</span>}
                        {periodo.contasAbertas !== 0 && (
                          <span className="block text-xs font-normal text-slate-500">
                            conta nova com {formatarValor(periodo.contasAbertas)}
                          </span>
                        )}
                      </th>
                      <td className="px-4 py-2 text-right text-emerald-700">{valorOuTraco(periodo.entradas)}</td>
                      <td className="px-4 py-2 text-right text-red-700">{valorOuTraco(periodo.saidas)}</td>
                      <td className="px-4 py-2 text-right text-slate-700">{valorOuTraco(periodo.aReceber)}</td>
                      <td className="px-4 py-2 text-right text-slate-700">{valorOuTraco(periodo.aPagar)}</td>
                      <td
                        className={
                          "px-4 py-2 text-right font-semibold " + (periodo.saldo < 0 ? "text-red-700" : "text-slate-800")
                        }
                      >
                        {formatarValor(periodo.saldo)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </div>
    </>
  );
}
