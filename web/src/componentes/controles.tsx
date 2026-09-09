"use client";

import type { InputHTMLAttributes, ReactNode, SelectHTMLAttributes, TextareaHTMLAttributes } from "react";
import { useId } from "react";

/**
 * Os primitivos de formulário.
 *
 * Poucos e concretos de propósito. A decisão Q20 descartou o motor de telas
 * declarativas do protótipo, e a ideia volta quando a segunda ou terceira tela
 * mostrar o que de fato se repete — generalizar na primeira é adivinhar a
 * forma.
 */

const baseDoCampo =
  "w-full rounded-[--radius-controle] border bg-superficie px-3 py-2 text-slate-800 " +
  "transition-colors placeholder:text-slate-400 disabled:bg-slate-50 disabled:text-slate-500";

function bordaDoCampo(temErro: boolean) {
  return temErro
    ? "border-red-500 focus:border-red-600"
    : "border-borda-forte focus:border-marca-500";
}

interface RotuloDeCampo {
  rotulo: string;
  erro?: string;
  ajuda?: string;
  children: (props: { id: string; descritoPor?: string; temErro: boolean }) => ReactNode;
}

export function Campo({ rotulo, erro, ajuda, children }: RotuloDeCampo) {
  const id = useId();
  const idDaAjuda = ajuda ? `${id}-ajuda` : undefined;
  const idDoErro = erro ? `${id}-erro` : undefined;

  /* Erro e ajuda são anunciados juntos: quem usa leitor de tela precisa dos dois. */
  const descritoPor = [idDoErro, idDaAjuda].filter(Boolean).join(" ") || undefined;

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-xs font-semibold tracking-wide text-slate-600 uppercase">
        {rotulo}
      </label>

      {children({ id, descritoPor, temErro: Boolean(erro) })}

      {erro && (
        <p id={idDoErro} className="text-xs text-red-700">
          {erro}
        </p>
      )}
      {ajuda && !erro && (
        <p id={idDaAjuda} className="text-xs text-slate-500">
          {ajuda}
        </p>
      )}
    </div>
  );
}

type PropsDeEntrada = Omit<InputHTMLAttributes<HTMLInputElement>, "id"> & {
  rotulo: string;
  erro?: string;
  ajuda?: string;
};

export function Entrada({ rotulo, erro, ajuda, className, ...resto }: PropsDeEntrada) {
  return (
    <Campo rotulo={rotulo} erro={erro} ajuda={ajuda}>
      {({ id, descritoPor, temErro }) => (
        <input
          {...resto}
          id={id}
          aria-invalid={temErro || undefined}
          aria-describedby={descritoPor}
          className={`${baseDoCampo} ${bordaDoCampo(temErro)} ${className ?? ""}`}
        />
      )}
    </Campo>
  );
}

type PropsDeSelecao = Omit<SelectHTMLAttributes<HTMLSelectElement>, "id"> & {
  rotulo: string;
  erro?: string;
  ajuda?: string;
};

export function Selecao({ rotulo, erro, ajuda, className, children, ...resto }: PropsDeSelecao) {
  return (
    <Campo rotulo={rotulo} erro={erro} ajuda={ajuda}>
      {({ id, descritoPor, temErro }) => (
        <select
          {...resto}
          id={id}
          aria-invalid={temErro || undefined}
          aria-describedby={descritoPor}
          className={`${baseDoCampo} ${bordaDoCampo(temErro)} ${className ?? ""}`}
        >
          {children}
        </select>
      )}
    </Campo>
  );
}

type PropsDeTexto = Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, "id"> & {
  rotulo: string;
  erro?: string;
  ajuda?: string;
};

export function AreaDeTexto({ rotulo, erro, ajuda, className, ...resto }: PropsDeTexto) {
  return (
    <Campo rotulo={rotulo} erro={erro} ajuda={ajuda}>
      {({ id, descritoPor, temErro }) => (
        <textarea
          {...resto}
          id={id}
          aria-invalid={temErro || undefined}
          aria-describedby={descritoPor}
          className={`${baseDoCampo} ${bordaDoCampo(temErro)} min-h-24 ${className ?? ""}`}
        />
      )}
    </Campo>
  );
}

type Aparencia = "principal" | "secundario" | "perigo";

const aparencias: Record<Aparencia, string> = {
  principal: "bg-marca-600 text-white hover:bg-marca-700 disabled:bg-marca-300",
  secundario:
    "bg-superficie text-slate-700 border border-borda-forte hover:bg-slate-50 disabled:text-slate-400",
  perigo: "bg-red-600 text-white hover:bg-red-700 disabled:bg-red-300",
};

export function Botao({
  aparencia = "principal",
  className,
  ...resto
}: InputHTMLAttributes<HTMLButtonElement> & { aparencia?: Aparencia; type?: "button" | "submit" }) {
  return (
    <button
      {...resto}
      className={
        "inline-flex items-center justify-center gap-2 rounded-[--radius-controle] px-4 py-2 " +
        "text-sm font-semibold transition-colors disabled:cursor-not-allowed " +
        `${aparencias[aparencia]} ${className ?? ""}`
      }
    />
  );
}
