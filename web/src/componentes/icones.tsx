/**
 * Os ícones da aplicação, em traçado.
 *
 * <b>Uma família só.</b> Todos partem do mesmo `viewBox` de 24, com traço de
 * 1.8 e pontas arredondadas, e herdam a cor do texto. Misturar espessuras ou
 * pegar cada um de uma fonte diferente é o que faz uma tela parecer montada às
 * pressas, mesmo quando cada peça isolada está bonita.
 *
 * <b>Nada de emoji.</b> Emoji muda de desenho a cada sistema, não obedece cor
 * nem peso, e não tem como ficar alinhado ao texto do lado.
 */

type Props = { className?: string };

function Traco({ className = "size-4", children }: Props & { children: React.ReactNode }) {
  return (
    <svg
      viewBox="0 0 24 24"
      aria-hidden="true"
      className={className}
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      {children}
    </svg>
  );
}

export function IconeDeBusca({ className }: Props) {
  return (
    <Traco className={className}>
      <circle cx="11" cy="11" r="7" />
      <path d="m20 20-3.5-3.5" />
    </Traco>
  );
}

export function IconeDeFiltro({ className }: Props) {
  return (
    <Traco className={className}>
      <path d="M3 5h18l-7 8v6l-4 2v-8z" />
    </Traco>
  );
}

export function IconeDeColunas({ className }: Props) {
  return (
    <Traco className={className}>
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <path d="M9 4v16M15 4v16" />
    </Traco>
  );
}

export function IconeDeMaisAcoes({ className }: Props) {
  return (
    <Traco className={className}>
      <circle cx="12" cy="5" r="1" />
      <circle cx="12" cy="12" r="1" />
      <circle cx="12" cy="19" r="1" />
    </Traco>
  );
}

export function IconeDeCopiar({ className }: Props) {
  return (
    <Traco className={className}>
      <rect x="9" y="9" width="11" height="11" rx="2" />
      <path d="M5 15V5a2 2 0 0 1 2-2h8" />
    </Traco>
  );
}

export function IconeDeAbrir({ className }: Props) {
  return (
    <Traco className={className}>
      <path d="M14 4h6v6" />
      <path d="M20 4 11 13" />
      <path d="M18 14v4a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4" />
    </Traco>
  );
}

export function IconeDeSeta({ className }: Props) {
  return (
    <Traco className={className}>
      <path d="m6 9 6 6 6-6" />
    </Traco>
  );
}

/**
 * O desenho do estado vazio, maior e mais leve que os outros.
 *
 * Traço de 1.2 em vez de 1.8: no tamanho em que ele aparece, 1.8 vira um
 * rabisco pesado no meio de uma área que deveria respirar.
 */
export function DesenhoDeCadastroVazio({ className = "size-12" }: Props) {
  return (
    <svg
      viewBox="0 0 48 48"
      aria-hidden="true"
      className={className}
      fill="none"
      stroke="currentColor"
      strokeWidth="1.2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <rect x="7" y="10" width="34" height="28" rx="3" />
      <path d="M7 18h34" />
      <circle cx="18" cy="27" r="3.5" />
      <path d="M12.5 34a6 6 0 0 1 11 0" />
      <path d="M28 25h9M28 30h7" />
    </svg>
  );
}
