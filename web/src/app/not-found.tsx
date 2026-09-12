import Link from "next/link";

/**
 * Endereço que não existe.
 *
 * Sem estilo próprio, o Next mostra a página preta e branca dele — que não
 * parece um erro do sistema, parece o sistema ter sumido.
 *
 * Não reaproveita a `TelaDeErro` porque esta não é um erro: ninguém precisa
 * tentar de novo, e não há código para copiar. É só uma porta que não leva a
 * lugar nenhum.
 */
export default function NaoEncontrado() {
  return (
    <div className="flex min-h-dvh items-center justify-center p-6">
      <div className="w-full max-w-md rounded-[--radius-cartao] border border-borda bg-superficie p-8 text-center shadow-nivel-1">
        <p className="numeros-tabulares text-3xl font-semibold text-marca-200">404</p>

        <h1 className="mt-2 text-xl font-semibold tracking-tight text-marca-950">
          Esta página não existe
        </h1>
        <p className="mt-2 text-slate-600">
          O endereço pode ter mudado, ou o link estar errado.
        </p>

        <Link
          href="/pessoas"
          className="mt-6 inline-flex items-center rounded-[--radius-controle] bg-marca-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-marca-700"
        >
          Ir para Pessoas
        </Link>
      </div>
    </div>
  );
}
