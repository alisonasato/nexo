import { FormularioDePessoa } from "@/componentes/formulario-de-pessoa";

/*
 * `params` é uma Promise desde o Next 15, e o tipo é escrito à mão em vez de
 * usar `PageProps<"/pessoas/[id]">`: os tipos de rota gerados só existem depois
 * de um `next dev` ou `next build`, então depender deles quebra a verificação
 * num clone limpo.
 */
export default async function EditarPessoa({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <FormularioDePessoa id={id} />;
}
