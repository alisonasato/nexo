/** Valores e datas em português do Brasil. */

const moeda = new Intl.NumberFormat("pt-BR", {
  style: "currency",
  currency: "BRL",
});

export function formatarValor(valor: number): string {
  return moeda.format(valor);
}

/**
 * A máscara do campo de dinheiro, preenchida da direita para a esquerda.
 *
 * Os dígitos entram pelos centavos: 1 vira 0,01, depois 0,12, depois 1,23. É
 * como todo sistema financeiro brasileiro se comporta, e resolve por construção
 * um problema que era real aqui.
 *
 * A versão anterior lia o texto livre e tinha de adivinhar o que o ponto queria
 * dizer. Errava: `1.500` virava 1,50, e `1.234.567` virava **zero**, porque o
 * `Number` do JavaScript devolvia `NaN`. Um contrato de um milhão entrava
 * valendo nada, em silêncio.
 *
 * Não há mais o que adivinhar, porque a vírgula nunca é digitada.
 *
 * Recebe e devolve texto. O que se guarda é a sequência de dígitos, e o valor
 * numérico sai de <see cref="valorDosDigitos"/>.
 */
export function mascararDinheiro(texto: string): string {
  /* Treze dígitos dão até onze casas antes da vírgula. Nenhum honorário
     contábil chega perto, e o teto evita número que nem cabe na tela. */
  const digitos = texto.replace(/\D/g, "").slice(0, 13).replace(/^0+(?=\d)/, "");
  if (!digitos) return "";

  const comCentavos = digitos.padStart(3, "0");
  const inteiros = comCentavos.slice(0, -2);
  const centavos = comCentavos.slice(-2);

  return inteiros.replace(/\B(?=(\d{3})+(?!\d))/g, ".") + "," + centavos;
}

/** O número que a sequência de dígitos representa: sempre centavos. */
export function valorDosDigitos(digitos: string): number {
  const numero = digitos.replace(/\D/g, "");
  return numero ? Number(numero) / 100 : 0;
}

/** O caminho inverso, para preencher o campo com um valor que já existe. */
export function digitosDoValor(valor: number | null | undefined): string {
  if (valor === null || valor === undefined || !Number.isFinite(valor)) return "";
  return String(Math.round(valor * 100));
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
