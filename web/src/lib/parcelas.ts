/**
 * A prévia das parcelas, com as mesmas regras da API.
 *
 * <b>Quem manda é o servidor.</b> Esta conta existe só para a pessoa ver, antes
 * de confirmar, quanto e quando cada parcela vence. O que vai para o banco é o
 * que a API divide, com as regras de <code>Dominio/Parcelas.cs</code>: centavos
 * inteiros, sobra na primeira parcela, e cada vencimento a partir da primeira
 * data. Se as duas contas divergirem um dia, a tela mostrou uma prévia errada,
 * e o dinheiro continua certo.
 */

export const MAXIMO_DE_PARCELAS = 60;

export type ParcelaPrevista = { numero: number; centavos: number; vencimento: string };

/**
 * Soma meses a uma data ISO, prendendo o dia no fim do mês quando ele não
 * existe: 31 de janeiro mais um mês é 28 de fevereiro, e mais dois é 31 de
 * março. Sem passar por fuso horário.
 */
export function somarMeses(iso: string, meses: number): string {
  const [ano, mes, dia] = iso.slice(0, 10).split("-").map(Number);
  const indice = ano * 12 + (mes - 1) + meses;
  const anoFinal = Math.floor(indice / 12);
  const mesFinal = indice - anoFinal * 12;
  const diasNoMes = new Date(Date.UTC(anoFinal, mesFinal + 1, 0)).getUTCDate();

  const mm = String(mesFinal + 1).padStart(2, "0");
  const dd = String(Math.min(dia, diasNoMes)).padStart(2, "0");
  return `${anoFinal}-${mm}-${dd}`;
}

/** Devolve lista vazia quando não há o que prever, em vez de lançar erro no meio da digitação. */
export function dividirEmParcelas(
  totalCentavos: number,
  quantidade: number,
  primeiroVencimento: string,
): ParcelaPrevista[] {
  if (!Number.isInteger(quantidade) || quantidade < 1 || quantidade > MAXIMO_DE_PARCELAS) return [];
  if (!/^\d{4}-\d{2}-\d{2}/.test(primeiroVencimento) || totalCentavos < quantidade) return [];

  const porParcela = Math.floor(totalCentavos / quantidade);
  const sobra = totalCentavos - porParcela * quantidade;

  return Array.from({ length: quantidade }, (_, indice) => ({
    numero: indice + 1,
    centavos: porParcela + (indice === 0 ? sobra : 0),
    vencimento: somarMeses(primeiroVencimento, indice),
  }));
}

/** O número de parcelas digitado, preso entre 1 e o máximo. */
export function limitarParcelas(texto: string): number {
  const numero = Math.trunc(Number(texto));
  return Number.isFinite(numero) && numero >= 1 ? Math.min(numero, MAXIMO_DE_PARCELAS) : 1;
}
