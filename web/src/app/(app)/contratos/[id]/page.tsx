import { FormularioDeContrato } from "@/componentes/formulario-de-contrato";

export default async function EditarContrato({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <FormularioDeContrato id={id} />;
}
