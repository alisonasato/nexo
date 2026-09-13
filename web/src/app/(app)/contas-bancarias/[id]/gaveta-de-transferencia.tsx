"use client";

import { useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { useAvisos } from "@/componentes/avisos";
import { Botao, Entrada, EntradaMascarada, Selecao } from "@/componentes/controles";
import { Gaveta } from "@/componentes/gaveta";
import { hojeIso, mascararDinheiro, valorDosDigitos } from "@/lib/dinheiro";

type Problema = components["schemas"]["Problema"];

const camposDaGaveta = ["destinoId", "data", "valor", "descricao"];

function problemasDaResposta(erro: unknown): Problema[] {
  if (erro && typeof erro === "object" && "problemas" in erro && Array.isArray(erro.problemas)) {
    return erro.problemas as Problema[];
  }
  return [];
}

type Props = { origemId: string; aberta: boolean; aoFechar: () => void; aoGravar: () => void };

/**
 * Transferir desta conta para outra do escritório.
 *
 * A origem é a conta do extrato aberto, e não se escolhe: quem transfere está
 * olhando de onde o dinheiro sai. A lista de destino mostra só as outras contas
 * ativas, que são as únicas que a API aceitaria.
 */
export function GavetaDeTransferencia({ origemId, aberta, aoFechar, aoGravar }: Props) {
  const avisar = useAvisos();

  const [destino, definirDestino] = useState("");
  const [data, definirData] = useState(hojeIso());
  const [digitos, definirDigitos] = useState("");
  const [descricao, definirDescricao] = useState("");
  const [problemas, definirProblemas] = useState<Problema[]>([]);

  const contas = useQuery({
    queryKey: ["contas-bancarias"],
    queryFn: async () => {
      const { data: lista, error } = await api.GET("/contas-bancarias");
      if (error || !lista) throw new Error("Não foi possível carregar as contas.");
      return lista;
    },
    enabled: aberta,
  });

  const destinos = (contas.data ?? []).filter((conta) => conta.ativa && conta.id !== origemId);
  const destinoEscolhido = destinos.some((conta) => conta.id === destino) ? destino : (destinos[0]?.id ?? "");

  const transferir = useMutation({
    mutationFn: async () => {
      const { error } = await api.POST("/contas-bancarias/transferencias", {
        body: { origemId, destinoId: destinoEscolhido, data, valor: valorDosDigitos(digitos), descricao },
      });
      if (error) throw error;
    },
    onSuccess: () => {
      aoGravar();
      avisar({ tom: "sucesso", titulo: "Transferência feita." });
      aoFechar();
    },
    onError: (erro: unknown) => definirProblemas(problemasDaResposta(erro)),
  });

  const erroDe = (campo: string) => {
    const problema = problemas.find((item) => item.campo === campo);
    return problema ? `${problema.descricao} ${problema.sugestao}` : undefined;
  };

  const deFora = problemas.filter((problema) => !camposDaGaveta.includes(problema.campo));
  const falhaSemMotivo = transferir.isError && problemas.length === 0;

  return (
    <Gaveta
      titulo="Transferir"
      descricao="Da conta deste extrato para outra do escritório. As duas pontas são gravadas juntas."
      aberta={aberta}
      aoFechar={aoFechar}
      rodape={
        <div className="flex justify-end gap-2">
          <Botao aparencia="secundario" type="button" onClick={aoFechar}>
            Cancelar
          </Botao>
          <Botao
            type="submit"
            form="formulario-de-transferencia"
            disabled={transferir.isPending || !digitos || !destinoEscolhido}
          >
            {transferir.isPending ? "Transferindo…" : "Transferir"}
          </Botao>
        </div>
      }
    >
      <form
        id="formulario-de-transferencia"
        className="flex flex-col gap-4"
        onSubmit={(evento) => {
          evento.preventDefault();
          transferir.mutate();
        }}
      >
        {(deFora.length > 0 || falhaSemMotivo) && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            {deFora.length > 0
              ? `${deFora[0].descricao} ${deFora[0].sugestao}`
              : "Não foi possível transferir. Tente de novo em instantes."}
          </p>
        )}

        {contas.data && destinos.length === 0 && (
          <p className="rounded-[--radius-controle] bg-amber-50 px-4 py-3 text-amber-900">
            Não há outra conta ativa para receber a transferência. Cadastre a conta de destino em Contas
            bancárias.
          </p>
        )}

        <Selecao
          rotulo="Para a conta"
          value={destinoEscolhido}
          onChange={(evento) => definirDestino(evento.target.value)}
          erro={erroDe("destinoId")}
        >
          {destinos.map((conta) => (
            <option key={conta.id} value={conta.id}>
              {conta.nome}
            </option>
          ))}
        </Selecao>

        <div className="grid grid-cols-2 gap-4">
          <Entrada
            rotulo="Data"
            type="date"
            required
            value={data}
            onChange={(evento) => definirData(evento.target.value)}
            erro={erroDe("data")}
          />
          <EntradaMascarada
            rotulo="Valor"
            required
            className="text-right"
            digitos={digitos}
            mascara={mascararDinheiro}
            aoMudar={definirDigitos}
            erro={erroDe("valor")}
          />
        </div>

        <Entrada
          rotulo="Descrição"
          value={descricao}
          onChange={(evento) => definirDescricao(evento.target.value)}
          erro={erroDe("descricao")}
          ajuda="Opcional. Cada ponta leva junto o nome da outra conta."
        />
      </form>
    </Gaveta>
  );
}
