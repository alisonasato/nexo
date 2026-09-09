/** Valores e datas em português do Brasil. */

const moeda = new Intl.NumberFormat("pt-BR", {
  style: "currency",
  currency: "BRL",
});

export function formatarValor(valor: number): string {
  return moeda.format(valor);
}

/**
 * Converte o que a pessoa digitou em número.
 *
 * Aceita "1.234,56" e "1234.56": num formulário brasileiro as duas formas
 * aparecem, e recusar uma delas só transfere para o usuário um trabalho que o
 * código faz em três linhas.
 */
export function lerValor(texto: string): number {
  const limpo = texto.trim().replace(/[^\d,.-]/g, "");
  if (!limpo) return 0;

  const temVirgula = limpo.includes(",");
  const normalizado = temVirgula
    ? limpo.replace(/\./g, "").replace(",", ".")
    : limpo;

  const numero = Number(normalizado);
  return Number.isFinite(numero) ? numero : 0;
}

/** Uma data ISO (2026-03-10) em 10/03/2026, sem passar por fuso horário. */
export function formatarData(iso: string | null | undefined): string {
  if (!iso) return "—";
  const [ano, mes, dia] = iso.slice(0, 10).split("-");
  return `${dia}/${mes}/${ano}`;
}

const meses = [
  "janeiro", "fevereiro", "março", "abril", "maio", "junho",
  "julho", "agosto", "setembro", "outubro", "novembro", "dezembro",
];

export function formatarCompetencia(ano: number, mes: number): string {
  return `${meses[mes - 1] ?? mes}/${ano}`;
}

/** A competência de hoje, que é o padrão dos filtros e da geração. */
export function competenciaAtual(): { ano: number; mes: number } {
  const agora = new Date();
  return { ano: agora.getFullYear(), mes: agora.getMonth() + 1 };
}
