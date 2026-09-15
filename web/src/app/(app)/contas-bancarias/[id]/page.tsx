import { ExtratoDaConta } from "./extrato";

/*
 * `params` é uma Promise desde o Next 15, e o tipo é escrito à mão pelo mesmo
 * motivo de pessoas/[id]: os tipos de rota gerados só existem depois de um
 * `next dev` ou `next build`, e depender deles quebra a verificação num clone
 * limpo.
 */
export default async function ExtratoDaContaBancaria({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <ExtratoDaConta id={id} />;
}
