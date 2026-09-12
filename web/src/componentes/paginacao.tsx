"use client";

interface PropsDePaginacao {
  pagina: number;
  tamanho: number;
  total: number;
  aoMudar: (pagina: number) => void;
  /** Como chamar o que está sendo listado, para a contagem fazer sentido. */
  substantivo: { singular: string; plural: string };
  /** Quando informado, a barra ganha o seletor de itens por página. */
  aoMudarTamanho?: (tamanho: number) => void;
  /** Quando verdadeiro, os números de página aparecem entre Anterior e Próxima. */
  comNumeros?: boolean;
}

const TAMANHOS = [10, 25, 50, 100];

/**
 * A barra de paginação.
 *
 * Ela mostra o intervalo — "1 a 25 de 312" — e não só o número da página.
 * Página 7 de 13 não diz nada a quem está procurando um cliente; "151 a 175"
 * diz onde a pessoa está na lista.
 *
 * O seletor de itens por página e os números de página são <b>opcionais</b>.
 * Numa listagem de trinta linhas eles são controle a mais para decidir; numa de
 * três mil, são como se chega em algum lugar. Quem sabe qual é o caso é a tela.
 */
export function Paginacao({
  pagina,
  tamanho,
  total,
  aoMudar,
  substantivo,
  aoMudarTamanho,
  comNumeros = false,
}: PropsDePaginacao) {
  const paginas = Math.max(1, Math.ceil(total / tamanho));

  const contagem = total === 1 ? `1 ${substantivo.singular}` : `${total} ${substantivo.plural}`;

  const primeiro = total === 0 ? 0 : (pagina - 1) * tamanho + 1;
  const ultimo = Math.min(pagina * tamanho, total);

  const botao =
    "inline-flex min-h-9 items-center rounded-[--radius-controle] border border-borda-forte bg-superficie px-3 " +
    "font-medium text-slate-700 transition-colors hover:bg-slate-50 " +
    "focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-marca-500 " +
    "disabled:cursor-not-allowed disabled:text-slate-400 disabled:hover:bg-superficie";

  /* Sem seletor e com uma página só, não há o que controlar: fica só a contagem. */
  if (paginas <= 1 && !aoMudarTamanho) {
    return <p className="text-slate-600">{contagem}</p>;
  }

  return (
    <div className="flex flex-wrap items-center justify-between gap-3">
      <div className="flex flex-wrap items-center gap-4">
        <p className="numeros-tabulares text-slate-600">
          {total === 0 ? contagem : `Mostrando ${primeiro} a ${ultimo} de ${contagem}`}
        </p>

        {aoMudarTamanho && (
          <label className="flex items-center gap-2 text-slate-600">
            <span className="sr-only sm:not-sr-only">Por página</span>
            <select
              value={tamanho}
              onChange={(evento) => aoMudarTamanho(Number(evento.target.value))}
              aria-label="Itens por página"
              className="min-h-9 rounded-[--radius-controle] border border-borda-forte bg-superficie px-2 text-slate-700 focus:border-marca-500"
            >
              {TAMANHOS.map((opcao) => (
                <option key={opcao} value={opcao}>
                  {opcao}
                </option>
              ))}
            </select>
          </label>
        )}
      </div>

      {paginas > 1 && (
        <div className="flex items-center gap-2">
          <button
            type="button"
            className={botao}
            disabled={pagina <= 1}
            onClick={() => aoMudar(pagina - 1)}
          >
            Anterior
          </button>

          {comNumeros ? (
            <ol className="flex items-center gap-1">
              {numerosVisiveis(pagina, paginas).map((numero, indice) =>
                numero === null ? (
                  <li key={`corte-${indice}`} className="px-1 text-slate-400" aria-hidden="true">
                    …
                  </li>
                ) : (
                  <li key={numero}>
                    <button
                      type="button"
                      onClick={() => aoMudar(numero)}
                      aria-current={numero === pagina ? "page" : undefined}
                      aria-label={`Página ${numero}`}
                      className={
                        "numeros-tabulares inline-flex min-h-9 min-w-9 items-center justify-center rounded-[--radius-controle] px-2 font-medium transition-colors " +
                        "focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-marca-500 " +
                        (numero === pagina
                          ? "bg-marca-600 text-white"
                          : "border border-borda-forte bg-superficie text-slate-700 hover:bg-slate-50")
                      }
                    >
                      {numero}
                    </button>
                  </li>
                ),
              )}
            </ol>
          ) : (
            <span className="numeros-tabulares text-slate-600">
              {pagina} / {paginas}
            </span>
          )}

          <button
            type="button"
            className={botao}
            disabled={pagina >= paginas}
            onClick={() => aoMudar(pagina + 1)}
          >
            Próxima
          </button>
        </div>
      )}
    </div>
  );
}

/**
 * Os números que cabem, com reticências no lugar do que foi cortado.
 *
 * Mostrar todas as páginas funciona até a décima e vira uma régua ilegível na
 * centésima. A janela acompanha a página atual, e a primeira e a última ficam
 * sempre visíveis — são os dois destinos que alguém procura de olho fechado.
 */
function numerosVisiveis(atual: number, total: number): Array<number | null> {
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);

  const perto = [atual - 1, atual, atual + 1].filter((n) => n > 1 && n < total);
  const numeros = [1, ...perto, total];

  const comCortes: Array<number | null> = [];
  for (let i = 0; i < numeros.length; i++) {
    if (i > 0 && numeros[i] - numeros[i - 1] > 1) comCortes.push(null);
    comCortes.push(numeros[i]);
  }

  return comCortes;
}
