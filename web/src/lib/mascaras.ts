/**
 * Máscaras para o que está sendo digitado.
 *
 * Diferente de `formato.ts`, que formata valor pronto para leitura e devolve
 * o texto intacto quando o comprimento não bate. Aqui o valor está sempre pela
 * metade — é o estado normal de um campo em uso —, então a máscara cresce junto
 * com o que a pessoa digita.
 *
 * Todas recebem e devolvem texto e descartam a pontuação. O que se guarda é o
 * documento limpo: a máscara é da tela.
 */

import { apenasAlfanumericos, apenasDigitos } from "./formato";

export function mascararCpf(texto: string): string {
  const numero = apenasDigitos(texto).slice(0, 11);

  let saida = numero.slice(0, 3);
  if (numero.length > 3) saida += "." + numero.slice(3, 6);
  if (numero.length > 6) saida += "." + numero.slice(6, 9);
  if (numero.length > 9) saida += "-" + numero.slice(9, 11);

  return saida;
}

/**
 * CNPJ, que desde 31/07/2026 pode ter letras nas 12 primeiras posições.
 *
 * A pontuação fica onde sempre esteve — "12.ABC.345/01DE-35" —, porque a
 * quantidade de posições não mudou. Só o que cabe em cada uma mudou.
 */
export function mascararCnpj(texto: string): string {
  const numero = apenasAlfanumericos(texto).slice(0, 14);

  let saida = numero.slice(0, 2);
  if (numero.length > 2) saida += "." + numero.slice(2, 5);
  if (numero.length > 5) saida += "." + numero.slice(5, 8);
  if (numero.length > 8) saida += "/" + numero.slice(8, 12);
  if (numero.length > 12) saida += "-" + numero.slice(12, 14);

  return saida;
}

/**
 * Telefone com ou sem o nono dígito.
 *
 * Até dez dígitos a máscara supõe fixo, `(11) 3456-7890`. No décimo primeiro
 * ela vira celular e o hífen anda uma casa, `(11) 98765-4321`. O pulo é
 * inevitável: os dois formatos são indistinguíveis enquanto a pessoa não
 * terminar de digitar.
 */
export function mascararTelefone(texto: string): string {
  const numero = apenasDigitos(texto).slice(0, 11);
  if (numero.length === 0) return "";

  const celular = numero.length > 10;

  let saida = "(" + numero.slice(0, 2);
  if (numero.length > 2) saida += ") " + (celular ? numero.slice(2, 7) : numero.slice(2, 6));
  if (celular) saida += "-" + numero.slice(7, 11);
  else if (numero.length > 6) saida += "-" + numero.slice(6, 10);

  return saida;
}

export function mascararCep(texto: string): string {
  const numero = apenasDigitos(texto).slice(0, 8);
  return numero.length > 5 ? `${numero.slice(0, 5)}-${numero.slice(5)}` : numero;
}

/**
 * Onde o cursor precisa ficar para continuar depois do n-ésimo caractere que
 * conta.
 *
 * A máscara reescreve o campo inteiro a cada tecla, e sem isto o cursor
 * saltaria para o fim — o que só não incomoda quem digita do começo ao fim sem
 * errar.
 *
 * O que conta depende do campo, e por isso vem de fora: no telefone são os
 * dígitos, no CNPJ também são as letras. Quem decide é a própria função de
 * limpeza do campo, em vez de uma segunda regra que poderia discordar dela.
 */
export function indiceApos(
  texto: string,
  quantosDigitos: number,
  limpar: (texto: string) => string = apenasDigitos,
): number {
  if (quantosDigitos <= 0) return 0;

  let vistos = 0;
  for (let indice = 0; indice < texto.length; indice++) {
    if (limpar(texto[indice]).length > 0) {
      vistos++;
      if (vistos === quantosDigitos) return indice + 1;
    }
  }

  return texto.length;
}
