"use client";

interface PropsDePaginacao {
  pagina: number;
  tamanho: number;
  total: number;
  aoMudar: (pagina: number) => void;
  /** Como chamar o que está sendo listado, para a contagem fazer sentido. */
  substantivo: { singular: string; plural: string };
}

/**
 * A barra de paginação.
 *
 * Ela mostra o intervalo — "1 a 25 de 312" — e não só o número da página.
 * Página 7 de 13 não diz nada a quem está procurando um cliente; "151 a 175"
 * diz onde a pessoa está na lista.
 *
 * Some sozinha quando tudo cabe numa página: um controle que só tem uma opção
 * não é um controle, é ruído.
 */
export function Paginacao({ pagina, tamanho, total, aoMudar, substantivo }: PropsDePaginacao) {
  const paginas = Math.max(1, Math.ceil(total / tamanho));

  const contagem =
    total === 1 ? `1 ${substantivo.singular}` : `${total} ${substantivo.plural}`;

  if (paginas <= 1) {
    return <p className="text-slate-500">{contagem}</p>;
  }

  const primeiro = (pagina - 1) * tamanho + 1;
  const ultimo = Math.min(pagina * tamanho, total);

  const botao =
    "rounded-[--radius-controle] border border-borda-forte bg-superficie px-3 py-1.5 " +
    "font-medium text-slate-700 transition-colors hover:bg-slate-50 " +
    "disabled:cursor-not-allowed disabled:text-slate-400 disabled:hover:bg-superficie";

  return (
    <div className="flex flex-wrap items-center justify-between gap-3">
      <p className="numeros-tabulares text-slate-500">
        {primeiro} a {ultimo} de {contagem}
      </p>

      <div className="flex items-center gap-2">
        <button
          type="button"
          className={botao}
          disabled={pagina <= 1}
          onClick={() => aoMudar(pagina - 1)}
        >
          Anterior
        </button>

        <span className="numeros-tabulares text-slate-500">
          {pagina} / {paginas}
        </span>

        <button
          type="button"
          className={botao}
          disabled={pagina >= paginas}
          onClick={() => aoMudar(pagina + 1)}
        >
          Próxima
        </button>
      </div>
    </div>
  );
}
