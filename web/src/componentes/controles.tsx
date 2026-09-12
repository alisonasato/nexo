"use client";

import type {
  ChangeEvent,
  InputHTMLAttributes,
  ReactNode,
  SelectHTMLAttributes,
  TextareaHTMLAttributes,
} from "react";
import { useId, useLayoutEffect, useRef } from "react";

import { apenasDigitos } from "@/lib/formato";
import { indiceApos } from "@/lib/mascaras";

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

type PropsDeEntradaMascarada = Omit<
  InputHTMLAttributes<HTMLInputElement>,
  "id" | "value" | "onChange"
> & {
  rotulo: string;
  erro?: string;
  ajuda?: string;
  /** O que está guardado: o documento sem pontuação. */
  digitos: string;
  /** Como isso aparece enquanto a pessoa digita. */
  mascara: (texto: string) => string;
  /** Recebe o valor já limpo e limitado pela máscara. */
  aoMudar: (digitos: string) => void;
  /**
   * O que sobrevive à limpeza. Dígitos, por padrão.
   *
   * O CNPJ passa `apenasAlfanumericos`, porque desde 31/07/2026 ele pode ter
   * letras. É um valor só, e não uma regra repetida: a mesma função limpa o que
   * foi digitado e diz ao cursor o que contar.
   */
  limpar?: (texto: string) => string;
};

/**
 * Campo que se formata sozinho enquanto se digita.
 *
 * <b>Guarda dígitos, mostra máscara.</b> O estado nunca vê pontuação, então
 * nada precisa ser limpo na hora de enviar, e a API continua recebendo o que
 * sempre recebeu.
 *
 * As duas sutilezas estão aqui dentro, e nenhuma é enfeite:
 *
 * O <b>cursor</b> é recolocado à mão. A máscara reescreve o campo inteiro a
 * cada tecla, e sem isso o cursor saltaria para o fim — o que só não incomoda
 * quem digita do começo ao fim sem errar. A conta é por dígitos, não por
 * caracteres, porque a pontuação anda de lugar.
 *
 * O <b>apagar</b> precisa de tratamento. Com o cursor logo depois de um ponto,
 * o navegador apaga o ponto, e a máscara o devolveria na mesma hora: a tecla
 * pareceria quebrada. Quando isso acontece, o dígito anterior vai junto, que é
 * o que a pessoa quis apagar.
 */
export function EntradaMascarada({
  rotulo,
  erro,
  ajuda,
  className,
  digitos,
  mascara,
  aoMudar,
  limpar = apenasDigitos,
  ...resto
}: PropsDeEntradaMascarada) {
  const referencia = useRef<HTMLInputElement>(null);
  const cursorPendente = useRef<number | null>(null);

  useLayoutEffect(() => {
    if (cursorPendente.current === null) return;
    referencia.current?.setSelectionRange(cursorPendente.current, cursorPendente.current);
    cursorPendente.current = null;
  });

  function aoDigitar(evento: ChangeEvent<HTMLInputElement>) {
    const bruto = evento.target.value;
    const cursor = evento.target.selectionStart ?? bruto.length;

    let novos = limpar(bruto);
    let ateOCursor = limpar(bruto.slice(0, cursor)).length;

    const apagou = (evento.nativeEvent as InputEvent).inputType === "deleteContentBackward";

    if (apagou && novos.length === digitos.length && ateOCursor > 0) {
      novos = novos.slice(0, ateOCursor - 1) + novos.slice(ateOCursor);
      ateOCursor -= 1;
    }

    const exibido = mascara(novos);
    cursorPendente.current = indiceApos(exibido, ateOCursor, limpar);

    aoMudar(limpar(exibido));
  }

  return (
    <Campo rotulo={rotulo} erro={erro} ajuda={ajuda}>
      {({ id, descritoPor, temErro }) => (
        <input
          /* `inputMode` antes do resto: é padrão, não imposição. O telefone
             pede `tel`, que abre o teclado com os parênteses. */
          inputMode="numeric"
          {...resto}
          ref={referencia}
          id={id}
          value={mascara(digitos)}
          onChange={aoDigitar}
          aria-invalid={temErro || undefined}
          aria-describedby={descritoPor}
          className={`${baseDoCampo} ${bordaDoCampo(temErro)} numeros-tabulares ${className ?? ""}`}
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
