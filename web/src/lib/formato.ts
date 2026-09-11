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

/** O endereço como a listagem mostra, em uma linha. */
export type EnderecoParaLer = {
  cep?: string;
  logradouro?: string;
  numero?: string;
  complemento?: string;
  cidade?: string;
  uf?: string;
};

/**
 * Monta "Rua Exemplo, 123, Apto 45 - 12345-678 - São Paulo/SP".
 *
 * Nenhum campo do endereço é obrigatório no cadastro, e quase todo cadastro
 * chega com metade deles — então o que falta some junto com a pontuação que o
 * acompanharia. Um endereço sem complemento não pode virar "Rua Exemplo, 123,
 * - 12345-678", que parece dado corrompido.
 *
 * Vazio devolve string vazia: quem exibe decide se isso vira travessão.
 */
export function formatarEndereco(endereco: EnderecoParaLer | null | undefined): string {
  if (!endereco) return "";

  const limpar = (valor?: string) => (valor ?? "").trim();

  const via = [limpar(endereco.logradouro), limpar(endereco.numero), limpar(endereco.complemento)]
    .filter(Boolean)
    .join(", ");

  const cep = limpar(endereco.cep) ? formatarCep(limpar(endereco.cep)) : "";

  const cidade = limpar(endereco.cidade);
  const uf = limpar(endereco.uf);
  const local = cidade && uf ? `${cidade}/${uf}` : cidade || uf;

  return [via, cep, local].filter(Boolean).join(" - ");
}
