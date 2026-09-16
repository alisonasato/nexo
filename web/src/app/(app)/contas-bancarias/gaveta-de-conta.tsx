"use client";

import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { problemasDaResposta } from "@/lib/problemas";
import { useAvisos } from "@/componentes/avisos";
import { Botao, Entrada, EntradaMascarada, Selecao } from "@/componentes/controles";
import { Gaveta } from "@/componentes/gaveta";
import { digitosDoValor, hojeIso, mascararDinheiro, valorDosDigitos } from "@/lib/dinheiro";

type ContaNaLista = components["schemas"]["ContaNaLista"];
type TipoConta = components["schemas"]["TipoConta"];
type Problema = components["schemas"]["Problema"];

export const rotulosDeTipo: Record<TipoConta, string> = {
  Corrente: "Conta corrente",
  Poupanca: "Poupança",
  Pagamento: "Conta de pagamento",
  Dinheiro: "Dinheiro em caixa",
};

/**
 * Nula com a gaveta fechada, "nova" para cadastrar, a conta para alterar. Quem
 * abre passa uma chave com a conta, para cada abertura começar do que está
 * gravado e não do que foi digitado na anterior.
 */
type Props = { conta: ContaNaLista | "nova" | null; aoFechar: () => void };

/**
 * Cadastrar ou alterar uma conta.
 *
 * <p>
 * <b>O saldo inicial é o do começo do dia</b>, e o formulário diz isso por
 * extenso. Quem copia o saldo do fim do dia e depois baixa um recebimento
 * daquela mesma data conta o dinheiro duas vezes.
 * </p>
 * <p>
 * O valor se digita sem sinal, como todo campo de dinheiro daqui, e a conta que
 * começa no cheque especial marca a caixa de saldo negativo. Um sinal de menos
 * no meio da máscara seria o caractere mais fácil de esquecer.
 * </p>
 * <p>
 * Depois do primeiro movimento, os campos do saldo inicial ficam travados, e a
 * gaveta diz por quê: a API recusaria, e descobrir isso só ao salvar seria
 * digitar à toa.
 * </p>
 */
export function GavetaDeConta({ conta, aoFechar }: Props) {
  const avisar = useAvisos();
  const clienteDeConsultas = useQueryClient();
  const existente = conta !== null && conta !== "nova" ? conta : null;
  const saldoTravado = existente?.temMovimentos ?? false;

  const [nome, definirNome] = useState(existente?.nome ?? "");
  const [tipo, definirTipo] = useState<TipoConta>(existente?.tipo ?? "Corrente");
  const [nomeDoBanco, definirNomeDoBanco] = useState(existente?.banco ?? "");
  const [agencia, definirAgencia] = useState(existente?.agencia ?? "");
  const [numero, definirNumero] = useState(existente?.numero ?? "");
  const [digitos, definirDigitos] = useState(existente ? digitosDoValor(Math.abs(existente.saldoInicial)) : "");
  const [negativo, definirNegativo] = useState((existente?.saldoInicial ?? 0) < 0);
  const [saldoInicialEm, definirSaldoInicialEm] = useState(existente?.saldoInicialEm ?? hojeIso());
  const [ativa, definirAtiva] = useState(existente?.ativa ?? true);
  const [recebeCobrancas, definirRecebeCobrancas] = useState(existente?.recebeCobrancas ?? false);
  const [problemas, definirProblemas] = useState<Problema[]>([]);

  const emDinheiro = tipo === "Dinheiro";

  const salvar = useMutation({
    mutationFn: async () => {
      const valor = valorDosDigitos(digitos);
      const body = {
        nome,
        tipo,
        banco: emDinheiro ? "" : nomeDoBanco,
        agencia: emDinheiro ? "" : agencia,
        numero: emDinheiro ? "" : numero,
        saldoInicial: negativo ? -valor : valor,
        saldoInicialEm,
        ativa,
        recebeCobrancas,
      };

      const { error } = existente
        ? await api.PUT("/contas-bancarias/{id}", { params: { path: { id: existente.id } }, body })
        : await api.POST("/contas-bancarias", { body });

      if (error) throw error;
    },
    onSuccess: () => {
      clienteDeConsultas.invalidateQueries({ queryKey: ["contas-bancarias"] });
      avisar({ tom: "sucesso", titulo: existente ? "Conta alterada." : "Conta cadastrada." });
      aoFechar();
    },
    onError: (erro: unknown) => definirProblemas(problemasDaResposta(erro)),
  });

  /* O que está errado e o que fazer, juntos: o campo mostra um texto só. */
  const erroDe = (campo: string) => {
    const problema = problemas.find((item) => item.campo === campo);
    return problema ? `${problema.descricao} ${problema.sugestao}` : undefined;
  };

  const falhaSemMotivo = salvar.isError && problemas.length === 0;
  const erroDaMarca = erroDe("recebeCobrancas");

  return (
    <Gaveta
      titulo={existente ? "Alterar conta" : "Nova conta"}
      descricao={existente ? existente.nome : "Uma conta onde o dinheiro do escritório entra ou sai."}
      aberta={conta !== null}
      aoFechar={aoFechar}
      rodape={
        <div className="flex justify-end gap-2">
          <Botao aparencia="secundario" type="button" onClick={aoFechar}>
            Cancelar
          </Botao>
          <Botao
            type="submit"
            form="formulario-de-conta"
            disabled={salvar.isPending || nome.trim().length === 0}
          >
            {salvar.isPending ? "Salvando…" : existente ? "Salvar" : "Cadastrar"}
          </Botao>
        </div>
      }
    >
      <form
        id="formulario-de-conta"
        className="flex flex-col gap-4"
        onSubmit={(evento) => {
          evento.preventDefault();
          salvar.mutate();
        }}
      >
        {falhaSemMotivo && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            Não foi possível salvar. Tente de novo em instantes.
          </p>
        )}

        <Entrada
          rotulo="Nome"
          required
          value={nome}
          onChange={(evento) => definirNome(evento.target.value)}
          erro={erroDe("nome")}
          ajuda="Como o escritório fala dela: “Itaú PJ”, “Asaas”, “Caixa da recepção”."
        />

        <Selecao
          rotulo="Tipo"
          value={tipo}
          onChange={(evento) => definirTipo(evento.target.value as TipoConta)}
          erro={erroDe("tipo")}
        >
          {(Object.entries(rotulosDeTipo) as [TipoConta, string][]).map(([valor, rotulo]) => (
            <option key={valor} value={valor}>
              {rotulo}
            </option>
          ))}
        </Selecao>

        {!emDinheiro && (
          <>
            <Entrada
              rotulo="Banco"
              value={nomeDoBanco}
              onChange={(evento) => definirNomeDoBanco(evento.target.value)}
              erro={erroDe("banco")}
            />
            <div className="grid grid-cols-2 gap-4">
              <Entrada
                rotulo="Agência"
                inputMode="numeric"
                value={agencia}
                onChange={(evento) => definirAgencia(evento.target.value)}
                erro={erroDe("agencia")}
              />
              <Entrada
                rotulo="Número da conta"
                value={numero}
                onChange={(evento) => definirNumero(evento.target.value)}
                erro={erroDe("numero")}
              />
            </div>
          </>
        )}

        <fieldset className="flex flex-col gap-3" disabled={saldoTravado}>
          <legend className="mb-1 text-xs font-semibold tracking-wide text-slate-600 uppercase">
            Saldo inicial
          </legend>

          <div className="grid grid-cols-2 gap-4">
            <EntradaMascarada
              rotulo="Valor"
              className="text-right"
              digitos={digitos}
              mascara={mascararDinheiro}
              aoMudar={definirDigitos}
              erro={erroDe("saldoInicial")}
            />
            <Entrada
              rotulo="No começo do dia"
              type="date"
              required
              value={saldoInicialEm}
              onChange={(evento) => definirSaldoInicialEm(evento.target.value)}
              erro={erroDe("saldoInicialEm")}
            />
          </div>

          <label className="flex items-center gap-2 text-slate-700">
            <input
              type="checkbox"
              checked={negativo}
              onChange={(evento) => definirNegativo(evento.target.checked)}
              className="size-4 rounded border-borda-forte accent-marca-600"
            />
            Saldo negativo: a conta começa no cheque especial
          </label>

          <p className="text-sm text-slate-600">
            {saldoTravado
              ? "A conta já tem movimentos, e o saldo inicial está por baixo de todos eles. Para corrigi-lo, estorne as baixas desta conta antes."
              : "O saldo antes de qualquer entrada ou saída daquele dia. Escolha um dia que dê para conferir no extrato."}
          </p>
        </fieldset>

        <label className="flex items-start gap-2 text-slate-700">
          <input
            type="checkbox"
            checked={recebeCobrancas}
            onChange={(evento) => definirRecebeCobrancas(evento.target.checked)}
            aria-describedby={erroDaMarca ? "erro-da-marca" : undefined}
            className="mt-1 size-4 rounded border-borda-forte accent-marca-600"
          />
          <span>
            Recebe as cobranças do PSP
            <span className="block text-sm text-slate-500">
              É onde entra o que o cliente paga pelo boleto ou pelo Pix. Uma conta só: marcar esta tira a
              marca da outra.
            </span>
            {erroDaMarca && (
              <span id="erro-da-marca" className="block text-sm text-red-700">
                {erroDaMarca}
              </span>
            )}
          </span>
        </label>

        {existente && (
          <label className="flex items-start gap-2 text-slate-700">
            <input
              type="checkbox"
              checked={ativa}
              onChange={(evento) => definirAtiva(evento.target.checked)}
              className="mt-1 size-4 rounded border-borda-forte accent-marca-600"
            />
            <span>
              Conta ativa
              <span className="block text-sm text-slate-500">
                Conta encerrada fica inativa, e não se apaga: o dinheiro que passou por ela continua lá.
              </span>
            </span>
          </label>
        )}
      </form>
    </Gaveta>
  );
}
