"use client";

import { useState } from "react";
import { useMutation } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { useAvisos } from "@/componentes/avisos";
import { Botao, Entrada, EntradaMascarada, Selecao } from "@/componentes/controles";
import { Gaveta } from "@/componentes/gaveta";
import { hojeIso, mascararDinheiro, valorDosDigitos } from "@/lib/dinheiro";

type TipoDeMovimentoAvulso = components["schemas"]["TipoDeMovimentoAvulso"];
type Problema = components["schemas"]["Problema"];

const tipos: Record<TipoDeMovimentoAvulso, { rotulo: string; ajuda: string }> = {
  Tarifa: { rotulo: "Tarifa bancária, que sai", ajuda: "Opcional. Sem descrição, fica “Tarifa bancária”." },
  Rendimento: { rotulo: "Rendimento, que entra", ajuda: "Opcional. Sem descrição, fica “Rendimento”." },
  OutraEntrada: { rotulo: "Outra entrada", ajuda: "Diga o que foi: “Aporte do sócio”, “Devolução de caução”." },
  OutraSaida: { rotulo: "Outra saída", ajuda: "Diga o que foi: “Retirada do sócio”, “Pagamento de caução”." },
};

/* Os campos que esta gaveta mostra; o que a API recusar fora deles aparece no alto. */
const camposDaGaveta = ["tipo", "data", "valor", "descricao"];

function problemasDaResposta(erro: unknown): Problema[] {
  if (erro && typeof erro === "object" && "problemas" in erro && Array.isArray(erro.problemas)) {
    return erro.problemas as Problema[];
  }
  return [];
}

type Props = { contaId: string; aberta: boolean; aoFechar: () => void; aoGravar: () => void };

/**
 * Lançar o que o extrato do banco tem e não é lançamento de ninguém.
 *
 * O valor se digita sem sinal, e o tipo diz o lado. Um sinal de menos seria o
 * caractere mais fácil de esquecer, e a tarifa que entrasse somando mudaria o
 * saldo pelo dobro do valor.
 */
export function GavetaDeMovimento({ contaId, aberta, aoFechar, aoGravar }: Props) {
  const avisar = useAvisos();

  const [tipo, definirTipo] = useState<TipoDeMovimentoAvulso>("Tarifa");
  const [data, definirData] = useState(hojeIso());
  const [digitos, definirDigitos] = useState("");
  const [descricao, definirDescricao] = useState("");
  const [problemas, definirProblemas] = useState<Problema[]>([]);

  const lancar = useMutation({
    mutationFn: async () => {
      const { error } = await api.POST("/contas-bancarias/{id}/movimentos", {
        params: { path: { id: contaId } },
        body: { tipo, data, valor: valorDosDigitos(digitos), descricao },
      });
      if (error) throw error;
    },
    onSuccess: () => {
      aoGravar();
      avisar({ tom: "sucesso", titulo: "Movimento lançado." });
      aoFechar();
    },
    onError: (erro: unknown) => definirProblemas(problemasDaResposta(erro)),
  });

  const erroDe = (campo: string) => {
    const problema = problemas.find((item) => item.campo === campo);
    return problema ? `${problema.descricao} ${problema.sugestao}` : undefined;
  };

  const deFora = problemas.filter((problema) => !camposDaGaveta.includes(problema.campo));
  const falhaSemMotivo = lancar.isError && problemas.length === 0;

  return (
    <Gaveta
      titulo="Lançar movimento"
      descricao="O que o extrato do banco tem e não é lançamento: tarifa, rendimento, aporte, retirada."
      aberta={aberta}
      aoFechar={aoFechar}
      rodape={
        <div className="flex justify-end gap-2">
          <Botao aparencia="secundario" type="button" onClick={aoFechar}>
            Cancelar
          </Botao>
          <Botao type="submit" form="formulario-de-movimento" disabled={lancar.isPending || !digitos}>
            {lancar.isPending ? "Lançando…" : "Lançar"}
          </Botao>
        </div>
      }
    >
      <form
        id="formulario-de-movimento"
        className="flex flex-col gap-4"
        onSubmit={(evento) => {
          evento.preventDefault();
          lancar.mutate();
        }}
      >
        {(deFora.length > 0 || falhaSemMotivo) && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            {deFora.length > 0
              ? `${deFora[0].descricao} ${deFora[0].sugestao}`
              : "Não foi possível lançar. Tente de novo em instantes."}
          </p>
        )}

        <Selecao
          rotulo="Tipo"
          value={tipo}
          onChange={(evento) => definirTipo(evento.target.value as TipoDeMovimentoAvulso)}
          erro={erroDe("tipo")}
        >
          {(Object.entries(tipos) as [TipoDeMovimentoAvulso, { rotulo: string }][]).map(([valor, { rotulo }]) => (
            <option key={valor} value={valor}>
              {rotulo}
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
            ajuda="Sem sinal: o tipo diz o lado."
          />
        </div>

        <Entrada
          rotulo="Descrição"
          value={descricao}
          onChange={(evento) => definirDescricao(evento.target.value)}
          erro={erroDe("descricao")}
          ajuda={tipos[tipo].ajuda}
        />
      </form>
    </Gaveta>
  );
}
