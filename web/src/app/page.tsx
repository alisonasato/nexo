import { redirect } from "next/navigation";

/*
 * A raiz não tem conteúdo próprio. Quem tem sessão vai para o cadastro; quem
 * não tem é mandado para a entrada pela casca da aplicação.
 *
 * Aqui ficava a página que provava o contrato no passo 1. Ela cumpriu o papel
 * e saiu quando a primeira tela de verdade entrou.
 */
export default function Raiz() {
  redirect("/pessoas");
}
