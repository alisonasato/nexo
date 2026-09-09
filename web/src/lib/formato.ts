/** Formatação para leitura. O que se guarda é sempre só o número. */

export function apenasDigitos(texto: string): string {
  return texto.replace(/\D/g, "");
}

export function formatarDocumento(documento: string): string {
  const numero = apenasDigitos(documento);

  if (numero.length === 11) {
    return `${numero.slice(0, 3)}.${numero.slice(3, 6)}.${numero.slice(6, 9)}-${numero.slice(9)}`;
  }

  if (numero.length === 14) {
    return `${numero.slice(0, 2)}.${numero.slice(2, 5)}.${numero.slice(5, 8)}/${numero.slice(8, 12)}-${numero.slice(12)}`;
  }

  /* Comprimento inesperado: devolve como está, em vez de mutilar o dado. */
  return documento;
}

export function formatarTelefone(telefone: string): string {
  const numero = apenasDigitos(telefone);

  if (numero.length === 11) {
    return `(${numero.slice(0, 2)}) ${numero.slice(2, 7)}-${numero.slice(7)}`;
  }

  if (numero.length === 10) {
    return `(${numero.slice(0, 2)}) ${numero.slice(2, 6)}-${numero.slice(6)}`;
  }

  return telefone;
}

export function formatarCep(cep: string): string {
  const numero = apenasDigitos(cep);
  return numero.length === 8 ? `${numero.slice(0, 5)}-${numero.slice(5)}` : cep;
}
