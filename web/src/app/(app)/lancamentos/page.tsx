"use client";

import Link from "next/link";
import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import type { components } from "@/api/esquema";
import { useAvisos } from "@/componentes/avisos";
import { BarraDeSelecao } from "@/componentes/barra-de-selecao";
import { Botao, Entrada, EntradaMascarada, Selecao } from "@/componentes/controles";
import {
  IconeDeBusca,
  IconeDeCancelar,
  IconeDeClassificar,
  IconeDeCobranca,
  IconeDeCopiar,
  IconeDeHistorico,
  IconeDeRenegociar,
} from "@/componentes/icones";
import { MenuDeAcoes, type AcaoDeLinha } from "@/componentes/menu-de-acoes";
import { Paginacao } from "@/componentes/paginacao";
import { CabecalhoOrdenavel, useOrdenacao } from "@/componentes/tabela";
import { VoltarAoTopo } from "@/componentes/voltar-ao-topo";
import {
  digitosDoValor,
  formatarCompetencia,
  formatarData,
  formatarValor,
  hojeIso,
  mascararDinheiro,
  valorDosDigitos,
} from "@/lib/dinheiro";
import { useConsultaDaUrl } from "@/lib/estado-na-url";
import { useSelecaoPorConsulta } from "@/lib/selecao";

import { BaixaEmLote } from "./baixa-em-lote";
import { useCategorias, useCentrosDeCusto } from "./classificacao";
import { useContasParaBaixa } from "./contas";
import { AvisoDeDivergencias } from "./divergencias";
import { GavetaDeClassificacao } from "./gaveta-de-classificacao";
import { GavetaDeHistorico } from "./gaveta-de-historico";
import { GavetaDeLancamento } from "./gaveta-de-lancamento";
import { GavetaDeRenegociacao } from "./gaveta-de-renegociacao";

type NaturezaLancamento = components["schemas"]["NaturezaLancamento"];
type SituacaoLancamento = components["schemas"]["SituacaoLancamento"];
type LancamentoNaLista = components["schemas"]["LancamentoNaLista"];
type OrdemDeLancamentos = components["schemas"]["OrdemDeLancamentos"];
type Problema = components["schemas"]["Problema"];

/**
 * O que muda de uma natureza para a outra é quase só palavra.
 *
 * As regras são as mesmas dos dois lados, e por isso a tela é uma só. O que
 * não pode escapar é o texto: "recebido" numa conta a pagar lê como dinheiro
 * entrando, e a pessoa confere o extrato do lado errado.
 */
const textos = {
  Receber: {
    aba: "A receber",
    pessoa: "Cliente",
    busca: "Buscar por cliente, código ou descrição",
    aberto: "Em aberto",
    baixado: "Recebido",
    baixadoNaFrase: "recebido",
    vazio: "Nenhum lançamento a receber nesta seleção.",
    comoEntra:
      "As mensalidades saem dos contratos, em Contratos → Gerar mensalidades. O resto entra por Novo lançamento.",
  },
  Pagar: {
    aba: "A pagar",
    pessoa: "Fornecedor",
    busca: "Buscar por fornecedor, código ou descrição",
    aberto: "A pagar",
    baixado: "Pago",
    baixadoNaFrase: "pago",
    vazio: "Nenhuma conta a pagar nesta seleção.",
    comoEntra: "As contas do escritório entram por Novo lançamento, com o fornecedor marcado no cadastro.",
  },
} as const;

const naturezas = ["Receber", "Pagar"] as const;

/**
 * Os botões de ação: altura de dedo no celular, compactos na tabela.
 *
 * No cartão eles são a ação principal da linha, e 44 pixels é o mínimo que o
 * polegar acerta sem mirar. Na tabela, onde quem clica é ponteiro e há um por
 * linha, a altura cheia engordaria a lista sem ganho nenhum.
 */
const compacto = "min-h-11 px-4 text-xs md:min-h-0 md:px-3 md:py-1";

function problemasDaResposta(erro: unknown): Problema[] {
  if (erro && typeof erro === "object" && "problemas" in erro && Array.isArray(erro.problemas)) {
    return erro.problemas as Problema[];
  }
  return [];
}

function descreverFalha(erro: unknown, padrao: string): string {
  const problemas = problemasDaResposta(erro);
  return problemas.length > 0 ? `${problemas[0].descricao} ${problemas[0].sugestao}` : padrao;
}

type Estado = { rotulo: string; classe: string };

/**
 * A situação como a pessoa lê, com texto sempre junto da cor.
 *
 * Verde para pago, vermelho para vencido, âmbar para vence hoje e cinza para a
 * vencer. A cor ajuda a varrer a lista, mas quem não distingue vermelho de
 * verde lê a palavra, e a palavra está sempre lá.
 */
function estadoDe(item: LancamentoNaLista, hoje: string): Estado {
  switch (item.situacao) {
    case "Pago":
      return { rotulo: "Pago", classe: "bg-emerald-50 text-emerald-800" };
    case "Cancelado":
      return { rotulo: "Cancelado", classe: "bg-slate-100 text-slate-600 line-through" };
    case "Renegociado":
      return { rotulo: "Renegociado", classe: "bg-slate-100 text-slate-700" };
  }

  if (item.vencimento < hoje) return { rotulo: "Vencido", classe: "bg-red-50 text-red-800" };
  if (item.vencimento === hoje) return { rotulo: "Vence hoje", classe: "bg-amber-50 text-amber-800" };
  return { rotulo: "A vencer", classe: "bg-slate-100 text-slate-600" };
}

function corDoVencimento(item: LancamentoNaLista, hoje: string): string {
  if (item.situacao !== "Aberto") return "text-slate-700";
  if (item.vencimento < hoje) return "font-semibold text-red-700";
  if (item.vencimento === hoje) return "font-semibold text-amber-700";
  return "text-slate-700";
}

function Tarja({ estado }: { estado: Estado }) {
  return (
    <span className={`rounded-full px-2 py-0.5 text-xs font-semibold ${estado.classe}`}>
      {estado.rotulo}
    </span>
  );
}

/** "parcela 2 de 3", ou a marca de que veio de um acordo. Vazio para o comum. */
function rotuloDaParcela(item: LancamentoNaLista): string {
  if (item.parcelaNumero && item.parcelasTotal) {
    return ` · parcela ${item.parcelaNumero} de ${item.parcelasTotal}`;
  }
  return item.renegociadoDeId ? " · renegociação" : "";
}

/* Quem deu a baixa, que é a primeira pergunta quando o valor não bate. */
const origens: Record<string, string> = {
  manual: "à mão",
  lote: "em lote",
  cobranca: "pelo PSP",
};

/**
 * As abas de natureza, do jeito que o teclado e o leitor de tela esperam.
 *
 * Tab entra na aba escolhida e sai do grupo, e as setas trocam de aba. Um par
 * de botões soltos faria tabular pelos dois, e o leitor de tela não diria que
 * um deles está escolhido.
 */
function AbasDeNatureza({
  atual,
  aoTrocar,
}: {
  atual: NaturezaLancamento;
  aoTrocar: (natureza: NaturezaLancamento) => void;
}) {
  const abas = useRef<(HTMLButtonElement | null)[]>([]);

  function aoTeclar(evento: KeyboardEvent, indice: number) {
    if (evento.key !== "ArrowRight" && evento.key !== "ArrowLeft") return;
    evento.preventDefault();

    const passo = evento.key === "ArrowRight" ? 1 : -1;
    const proxima = (indice + passo + naturezas.length) % naturezas.length;

    aoTrocar(naturezas[proxima]);
    abas.current[proxima]?.focus();
  }

  return (
    <div role="tablist" aria-label="Natureza dos lançamentos" className="flex gap-1 border-b border-borda">
      {naturezas.map((natureza, indice) => {
        const escolhida = natureza === atual;

        return (
          <button
            key={natureza}
            ref={(botao) => {
              abas.current[indice] = botao;
            }}
            id={`aba-${natureza}`}
            type="button"
            role="tab"
            aria-selected={escolhida}
            aria-controls="painel-da-natureza"
            tabIndex={escolhida ? 0 : -1}
            onClick={() => aoTrocar(natureza)}
            onKeyDown={(evento) => aoTeclar(evento, indice)}
            className={
              "-mb-px inline-flex min-h-11 items-center border-b-2 px-4 font-semibold transition-colors " +
              (escolhida
                ? "border-marca-600 text-marca-950"
                : "border-transparent text-slate-500 hover:text-slate-800")
            }
          >
            {textos[natureza].aba}
          </button>
        );
      })}
    </div>
  );
}

export default function ListagemDeLancamentos() {
  const clienteDeConsultas = useQueryClient();
  const avisar = useAvisos();

  /*
   * Natureza, situação, busca, período, página e ordem moram na URL, como nas
   * outras listagens: a lista vira link, e "as contas a pagar vencidas deste
   * mês" passa a ser algo que se manda a alguém em vez de descrever por escrito.
   * A receber é o padrão e não aparece no endereço.
   *
   * As gavetas <b>não</b> moram lá. Elas não recortam a lista, e um link que já
   * abrisse um lançamento com pessoa e valor preenchidos seria um convite a
   * lançar o que ninguém conferiu.
   */
  const { ler, gravar, estabilizada } = useConsultaDaUrl("/lancamentos");

  const natureza: NaturezaLancamento = ler("natureza") === "Pagar" ? "Pagar" : "Receber";
  const situacao = (ler("situacao") ?? "") as SituacaoLancamento | "";
  const pagina = Math.max(1, Number(ler("pagina")) || 1);
  const busca = ler("busca") ?? "";
  const vencimentoDe = ler("vencimentoDe") ?? "";
  const vencimentoAte = ler("vencimentoAte") ?? "";

  /* "sem" pede o que ficou sem classificar; um id pede aquela categoria ou centro. */
  const categoriaFiltro = ler("categoria") ?? "";
  const centroFiltro = ler("centro") ?? "";
  const texto = textos[natureza];

  const { ordenarPor, ...ordem } = useOrdenacao<OrdemDeLancamentos>({ ler, gravar }, "Vencimento");

  /* Nulo enquanto ninguém digitou: o campo mostra o que a URL disser. */
  const [rascunho, definirRascunho] = useState<string | null>(null);
  const textoDaBusca = rascunho ?? busca;

  /* Meio segundo de espera: uma ida ao servidor por pausa, não por tecla. */
  useEffect(() => {
    if (rascunho === null || rascunho === busca) return;
    const relogio = setTimeout(() => gravar({ busca: rascunho, pagina: null }), 500);
    return () => clearTimeout(relogio);
  }, [rascunho, busca, gravar]);

  const definirPagina = (numero: number) => gravar({ pagina: numero === 1 ? null : numero });

  /*
   * Trocar de aba volta à primeira página e mantém o resto do recorte: "em
   * aberto, vencendo este mês" é uma pergunta que se faz dos dois lados.
   */
  const trocarNatureza = (nova: NaturezaLancamento) =>
    /* A categoria escolhida é de uma natureza só: do outro lado ela não existe. */
    gravar({ natureza: nova === "Receber" ? null : nova, pagina: null, categoria: null });

  /* As gavetas. */
  const [lancando, definirLancando] = useState(false);
  const [renegociando, definirRenegociando] = useState<LancamentoNaLista | null>(null);
  const [classificando, definirClassificando] = useState<LancamentoNaLista | null>(null);
  const [historicoAberto, definirHistoricoAberto] = useState<LancamentoNaLista | null>(null);
  const [baixandoEmLote, definirBaixandoEmLote] = useState(false);

  /* Os formulários que abrem dentro da própria linha. */
  const [baixando, definirBaixando] = useState<string | null>(null);
  const [valorDigitado, definirValorDigitado] = useState("");
  const [dataDaBaixa, definirDataDaBaixa] = useState(hojeIso());

  /* Desconto, juros e multa ficam atrás de um botão: a maior parte das baixas não tem nenhum. */
  const [comAcrescimos, definirComAcrescimos] = useState(false);
  const [descontoDigitado, definirDescontoDigitado] = useState("");
  const [jurosDigitado, definirJurosDigitado] = useState("");
  const [multaDigitada, definirMultaDigitada] = useState("");
  const [contaDaBaixa, definirContaDaBaixa] = useState("");
  const [falha, definirFalha] = useState<string | null>(null);
  const [cancelando, definirCancelando] = useState<string | null>(null);
  const [motivo, definirMotivo] = useState("");

  const consulta = { natureza, situacao, pagina, ordem, busca, vencimentoDe, vencimentoAte, categoriaFiltro, centroFiltro };

  const lancamentos = useQuery({
    queryKey: ["lancamentos", consulta],
    queryFn: async () => {
      const { data, error } = await api.GET("/lancamentos", {
        params: {
          query: {
            natureza,
            pagina,
            ordenarPor: ordem.por,
            direcao: ordem.direcao,
            ...(situacao ? { situacao } : {}),
            ...(busca ? { busca } : {}),
            ...(vencimentoDe ? { vencimentoDe } : {}),
            ...(vencimentoAte ? { vencimentoAte } : {}),
            ...(categoriaFiltro === "sem"
              ? { semCategoria: true }
              : categoriaFiltro
                ? { categoriaId: categoriaFiltro }
                : {}),
            ...(centroFiltro === "sem"
              ? { semCentroDeCusto: true }
              : centroFiltro
                ? { centroDeCustoId: centroFiltro }
                : {}),
          },
        },
      });
      if (error || !data) throw new Error("Não foi possível carregar os lançamentos.");
      return data;
    },
    /* Chegando por navegação, o primeiro render enxerga a URL da tela anterior:
       consultar ali sairia com o recorte errado. */
    enabled: estabilizada,
    placeholderData: keepPreviousData,
  });

  const selecao = useSelecaoPorConsulta(JSON.stringify(consulta));

  /* O caminho de cada categoria no plano, para a lista dizer "Ocupação › Aluguel", e não só "Aluguel". */
  const caminhos = new Map((useCategorias().data ?? []).map((categoria) => [categoria.id, categoria.caminho]));

  /* Para os filtros: as categorias desta aba e os centros, ativos ou não, porque o que já foi classificado continua achável. */
  const categoriasDaNatureza = (useCategorias().data ?? []).filter((categoria) => categoria.natureza === natureza);
  const centrosDoPlano = useCentrosDeCusto().data ?? [];

  /*
   * A conta da baixa lembra a última escolhida, que é quase sempre a mesma: a
   * do extrato que se está conferindo. Se ela saiu da lista, vale a primeira.
   */
  const contasParaBaixa = useContasParaBaixa().data ?? [];
  const contaEscolhida = contasParaBaixa.some((conta) => conta.id === contaDaBaixa)
    ? contaDaBaixa
    : (contasParaBaixa[0]?.id ?? "");

  const invalidar = () => {
    clienteDeConsultas.invalidateQueries({ queryKey: ["lancamentos"] });
    clienteDeConsultas.invalidateQueries({ queryKey: ["divergencias"] });
    /* A baixa e o estorno mudam o saldo da conta. */
    clienteDeConsultas.invalidateQueries({ queryKey: ["contas-bancarias"] });
  };

  const baixar = useMutation({
    mutationFn: async (id: string) => {
      const { data, error } = await api.POST("/lancamentos/{id}/baixar", {
        params: { path: { id } },
        body: {
          contaId: contaEscolhida,
          valorPago: valorDosDigitos(valorDigitado),
          pagoEm: dataDaBaixa,
          /* Vazio vai como nulo: sem nenhum dos três, o valor pago continua livre. */
          desconto: valorDosDigitos(descontoDigitado) || null,
          juros: valorDosDigitos(jurosDigitado) || null,
          multa: valorDosDigitos(multaDigitada) || null,
        },
      });
      if (error) throw error;
      return data;
    },
    onSuccess: () => {
      definirBaixando(null);
      definirFalha(null);
      invalidar();
    },
    onError: (erro: unknown) => definirFalha(descreverFalha(erro, "Não foi possível registrar a baixa.")),
  });

  const cancelar = useMutation({
    mutationFn: async (id: string) => {
      const { error } = await api.POST("/lancamentos/{id}/cancelar", {
        params: { path: { id } },
        body: { motivo },
      });
      if (error) throw error;
    },
    onSuccess: () => {
      definirCancelando(null);
      definirMotivo("");
      definirFalha(null);
      invalidar();
    },
    onError: (erro: unknown) => definirFalha(descreverFalha(erro, "Não foi possível cancelar.")),
  });

  const estornar = useMutation({
    mutationFn: async (id: string) => {
      const { error } = await api.POST("/lancamentos/{id}/estornar", {
        params: { path: { id } },
      });
      if (error) throw new Error("Não foi possível estornar.");
    },
    onSuccess: invalidar,
    onError: (erro) => avisar({ tom: "erro", titulo: erro.message }),
  });

  const cobrar = useMutation({
    mutationFn: async (id: string) => {
      const { data, error } = await api.POST("/lancamentos/{id}/cobrar", {
        params: { path: { id } },
      });
      if (error) throw error;
      return data!;
    },
    onSuccess: async (cobrado) => {
      invalidar();

      /*
       * O link vai direto para a área de transferência, porque o próximo passo
       * é mandá-lo ao cliente. Copiar depois de uma requisição pode ser
       * recusado pelo navegador, e aí o link aparece no aviso para copiar à mão.
       */
      try {
        await navigator.clipboard.writeText(cobrado.cobrancaUrl);
        avisar({ tom: "sucesso", titulo: "Cobrança emitida.", detalhe: "O link de pagamento foi copiado." });
      } catch {
        avisar({ tom: "sucesso", titulo: "Cobrança emitida.", detalhe: cobrado.cobrancaUrl });
      }
    },
    onError: (erro: unknown) =>
      avisar({
        tom: "erro",
        titulo: "A cobrança não foi emitida.",
        detalhe: descreverFalha(erro, "Tente de novo em instantes."),
      }),
  });

  const resumo = lancamentos.data;
  const itens = resumo?.itens ?? [];
  const hoje = hojeIso();

  /*
   * Só o que está em aberto se marca. A baixa em lote recusaria o resto de
   * qualquer jeito, e uma caixa marcável num título pago promete uma ação que
   * não existe.
   */
  const selecionaveis = itens.filter((item) => item.situacao === "Aberto");
  const todasMarcadas =
    selecionaveis.length > 0 && selecionaveis.every((item) => selecao.marcadas.has(item.id));
  const algumaMarcada = selecao.quantidade > 0;
  const selecionados = itens.filter((item) => selecao.marcadas.has(item.id));

  /*
   * As ações vivem aqui dentro porque dependem de meia dúzia de estados desta
   * tela: qual linha está em baixa, qual está em cancelamento, o que foi
   * digitado. Passar tudo isso para fora seria uma lista de propriedades maior
   * que o componente.
   */
  /* Com desconto, juros ou multa, o valor pago precisa fechar com eles: a conta já vem feita no campo. */
  function mudarAcrescimos(item: LancamentoNaLista, mudanca: { desconto?: string; juros?: string; multa?: string }) {
    const desconto = mudanca.desconto ?? descontoDigitado;
    const juros = mudanca.juros ?? jurosDigitado;
    const multa = mudanca.multa ?? multaDigitada;

    definirDescontoDigitado(desconto);
    definirJurosDigitado(juros);
    definirMultaDigitada(multa);

    const total = item.valor + valorDosDigitos(juros) + valorDosDigitos(multa) - valorDosDigitos(desconto);
    if (total > 0) definirValorDigitado(digitosDoValor(total));
  }

  /* Desconto, juros e multa dizem por que o pago difere do valor; sem eles, a diferença fica sem nome. */
  function acrescimosDe(item: LancamentoNaLista) {
    const partes = [
      item.juros ? `${formatarValor(item.juros)} de juros` : null,
      item.multa ? `${formatarValor(item.multa)} de multa` : null,
      item.desconto ? `${formatarValor(item.desconto)} de desconto` : null,
    ].filter(Boolean);
    return partes.length > 0 ? `com ${partes.join(", ")}` : null;
  }

  /* Todo lançamento tem histórico, em qualquer situação: é no cancelado que se pergunta quem cancelou. */
  function acaoDeHistorico(item: LancamentoNaLista): AcaoDeLinha {
    return {
      tipo: "botao",
      rotulo: "Histórico",
      icone: <IconeDeHistorico className="size-4" />,
      executar: () => {
        definirHistoricoAberto(item);
      },
    };
  }

  function acoes(item: LancamentoNaLista) {
    /* Cancelado e renegociado já não se movem: nas parcelas novas é que se age. Resta ver o que aconteceu. */
    if (item.situacao === "Cancelado" || item.situacao === "Renegociado") {
      return (
        <div className="flex items-center gap-1 md:justify-end">
          <MenuDeAcoes rotulo={`${item.nomeDaPessoa}, ${item.descricao}`} acoes={[acaoDeHistorico(item)]} />
        </div>
      );
    }

    if (cancelando === item.id) {
      return (
        <div className="flex flex-col items-stretch gap-2 md:items-end">
          <div className="md:w-56">
            <Entrada
              rotulo="Motivo do cancelamento"
              autoFocus
              value={motivo}
              onChange={(evento) => definirMotivo(evento.target.value)}
              ajuda="Quem olhar isto daqui a seis meses vai perguntar."
            />
          </div>

          <div className="flex gap-2 md:justify-end">
            <Botao
              aparencia="secundario"
              type="button"
              className={compacto}
              onClick={() => {
                definirCancelando(null);
                definirMotivo("");
                definirFalha(null);
              }}
            >
              Voltar
            </Botao>
            <Botao
              aparencia="perigo"
              type="button"
              className={compacto}
              disabled={cancelar.isPending || motivo.trim().length === 0}
              onClick={() => cancelar.mutate(item.id)}
            >
              {cancelar.isPending ? "Cancelando…" : "Confirmar"}
            </Botao>
          </div>

          {falha && (
            <p role="alert" className="text-xs text-red-700 md:max-w-xs md:text-right">
              {falha}
            </p>
          )}
        </div>
      );
    }

    if (item.situacao === "Pago") {
      return (
        <div className="flex items-center gap-1 md:justify-end">
          <Botao
            aparencia="secundario"
            type="button"
            className={compacto}
            disabled={estornar.isPending}
            onClick={() => estornar.mutate(item.id)}
          >
            Estornar
          </Botao>
          {/* O pago também se classifica: é nele que o relatório do mês procura. */}
          <MenuDeAcoes
            rotulo={`${item.nomeDaPessoa}, ${item.descricao}`}
            acoes={[
              {
                tipo: "botao",
                rotulo: "Classificar",
                icone: <IconeDeClassificar className="size-4" />,
                executar: () => {
                  definirClassificando(item);
                },
              },
              acaoDeHistorico(item),
            ]}
          />
        </div>
      );
    }

    if (baixando === item.id) {
      return (
        <div className="flex flex-col items-stretch gap-2 md:items-end">
          <div className="flex flex-wrap items-end gap-2 md:justify-end">
            <div className="w-32">
              <EntradaMascarada
                rotulo={textos[item.natureza].baixado}
                className="text-right"
                autoFocus
                digitos={valorDigitado}
                mascara={mascararDinheiro}
                aoMudar={definirValorDigitado}
              />
            </div>
            <div className="w-40">
              <Entrada
                rotulo="Data"
                type="date"
                value={dataDaBaixa}
                onChange={(evento) => definirDataDaBaixa(evento.target.value)}
              />
            </div>
            <div className="w-44">
              <Selecao
                rotulo="Conta"
                value={contaEscolhida}
                onChange={(evento) => definirContaDaBaixa(evento.target.value)}
              >
                {contasParaBaixa.map((conta) => (
                  <option key={conta.id} value={conta.id}>
                    {conta.nome}
                  </option>
                ))}
              </Selecao>
            </div>
          </div>

          <div className="flex flex-col items-stretch gap-2 md:items-end">
            <button
              type="button"
              aria-expanded={comAcrescimos}
              className="inline-flex min-h-11 items-center self-start text-xs font-semibold text-marca-700 underline-offset-2 hover:underline md:min-h-0 md:self-end"
              onClick={() => {
                /* Fechar desfaz: o valor volta ao do lançamento, sem acréscimo esquecido escondido. */
                if (comAcrescimos) mudarAcrescimos(item, { desconto: "", juros: "", multa: "" });
                definirComAcrescimos(!comAcrescimos);
              }}
            >
              {comAcrescimos ? "Sem desconto, juros ou multa" : "Desconto, juros ou multa"}
            </button>

            {comAcrescimos && (
              <div className="flex flex-wrap items-end gap-2 md:justify-end">
                <div className="w-28">
                  <EntradaMascarada
                    rotulo="Juros"
                    className="text-right"
                    digitos={jurosDigitado}
                    mascara={mascararDinheiro}
                    aoMudar={(digitos) => mudarAcrescimos(item, { juros: digitos })}
                  />
                </div>
                <div className="w-28">
                  <EntradaMascarada
                    rotulo="Multa"
                    className="text-right"
                    digitos={multaDigitada}
                    mascara={mascararDinheiro}
                    aoMudar={(digitos) => mudarAcrescimos(item, { multa: digitos })}
                  />
                </div>
                <div className="w-28">
                  <EntradaMascarada
                    rotulo="Desconto"
                    className="text-right"
                    digitos={descontoDigitado}
                    mascara={mascararDinheiro}
                    aoMudar={(digitos) => mudarAcrescimos(item, { desconto: digitos })}
                  />
                </div>
              </div>
            )}
          </div>

          {contasParaBaixa.length === 0 && (
            <p className="text-xs text-slate-600 md:max-w-xs md:text-right">
              Nenhuma conta ativa para receber a baixa.{" "}
              <Link href="/contas-bancarias" className="font-semibold underline underline-offset-2">
                Cadastre uma conta
              </Link>
              .
            </p>
          )}

          <div className="flex gap-2 md:justify-end">
            <Botao
              aparencia="secundario"
              type="button"
              className={compacto}
              onClick={() => {
                definirBaixando(null);
                definirFalha(null);
              }}
            >
              Cancelar
            </Botao>
            <Botao
              type="button"
              className={compacto}
              disabled={baixar.isPending || !contaEscolhida}
              onClick={() => baixar.mutate(item.id)}
            >
              {baixar.isPending ? "Registrando…" : "Confirmar"}
            </Botao>
          </div>

          {falha && (
            <p role="alert" className="text-xs text-red-700 md:max-w-xs md:text-right">
              {falha}
            </p>
          )}
        </div>
      );
    }

    /*
     * Baixar fica à vista porque é o que mais se faz. O resto vai para os três
     * pontos: uma linha de tabela com cinco botões vira barra de ferramentas, e
     * vinte linhas assim viram cem alvos disputando a atenção.
     */
    const menu: AcaoDeLinha[] = [];

    /* Cobrança pelo PSP é pedir dinheiro a um cliente: numa conta a pagar, quem paga é o escritório. */
    if (item.natureza === "Receber" && item.cobrancaUrl) {
      menu.push({
        tipo: "botao",
        rotulo: "Copiar link de pagamento",
        icone: <IconeDeCopiar className="size-4" />,
        executar: async () => {
          try {
            await navigator.clipboard.writeText(item.cobrancaUrl);
            return "Link copiado";
          } catch {
            return "Não foi possível copiar";
          }
        },
      });
    } else if (item.natureza === "Receber") {
      menu.push({
        tipo: "botao",
        rotulo: "Emitir cobrança",
        icone: <IconeDeCobranca className="size-4" />,
        executar: () => {
          cobrar.mutate(item.id);
        },
      });
    }

    menu.push({
      tipo: "botao",
      rotulo: "Classificar",
      icone: <IconeDeClassificar className="size-4" />,
      executar: () => {
        definirClassificando(item);
      },
    });

    menu.push({
      tipo: "botao",
      rotulo: "Renegociar",
      icone: <IconeDeRenegociar className="size-4" />,
      executar: () => {
        definirRenegociando(item);
      },
    });

    menu.push(acaoDeHistorico(item));

    menu.push({
      tipo: "botao",
      rotulo: "Cancelar",
      icone: <IconeDeCancelar className="size-4" />,
      executar: () => {
        definirCancelando(item.id);
        definirMotivo("");
        definirFalha(null);
      },
    });

    return (
      <div className="flex items-center gap-1 md:justify-end">
        <Botao
          aparencia="secundario"
          type="button"
          className={compacto}
          onClick={() => {
            definirBaixando(item.id);
            definirFalha(null);
            /* O valor do lançamento já vem preenchido: é o caso comum. */
            definirValorDigitado(digitosDoValor(item.valor));
            definirDataDaBaixa(hojeIso());
            definirComAcrescimos(false);
            definirDescontoDigitado("");
            definirJurosDigitado("");
            definirMultaDigitada("");
          }}
        >
          Baixar
        </Botao>
        <MenuDeAcoes rotulo={`${item.nomeDaPessoa}, ${item.descricao}`} acoes={menu} />
      </div>
    );
  }

  function classificacaoDe(item: LancamentoNaLista) {
    const categoria = item.categoriaId ? (caminhos.get(item.categoriaId) ?? item.categoria) : null;
    return [categoria, item.centroDeCusto].filter(Boolean).join(" · ");
  }

  function detalheDaBaixa(item: LancamentoNaLista) {
    if (!item.pagoEm) return null;
    const origem = origens[item.origemDaBaixa];
    const conta = item.contaDaBaixa ? ` · ${item.contaDaBaixa}` : "";
    return `${formatarData(item.pagoEm)}${origem ? ` · ${origem}` : ""}${conta}`;
  }

  return (
    <>
      <header className="flex flex-wrap items-center justify-between gap-4 border-b border-borda bg-superficie px-6 py-4">
        <div>
          <h1 id="conteudo" tabIndex={-1} className="text-xl font-semibold tracking-tight text-marca-950">
            Lançamentos
          </h1>
          <p className="text-slate-600">O que o escritório tem a receber e a pagar, e o que já foi baixado.</p>
        </div>

        <Botao type="button" onClick={() => definirLancando(true)}>
          Novo lançamento
        </Botao>
      </header>

      <div className="flex flex-1 flex-col gap-5 p-6">
        <AbasDeNatureza atual={natureza} aoTrocar={trocarNatureza} />

        <div
          role="tabpanel"
          id="painel-da-natureza"
          aria-labelledby={`aba-${natureza}`}
          className="flex flex-col gap-5"
        >
          {/* Divergência é pagamento que chegou pelo PSP, e o PSP só existe do lado a receber. */}
          {natureza === "Receber" && <AvisoDeDivergencias />}

          {resumo && (
            <div className="grid gap-px overflow-hidden rounded-[--radius-cartao] border border-borda bg-borda sm:grid-cols-3">
              {/*
                Estes números descrevem o período inteiro, não a página, a busca
                nem o filtro de situação: é a pergunta que o escritório faz
                enquanto olha uma fatia, quanto do mês ainda falta entrar ou
                sair. Categoria e centro de custo recortam também os totais,
                porque escolhem que parte do dinheiro medir.
              */}
              {[
                { rotulo: texto.aberto, valor: resumo.totalEmAberto, tom: "text-slate-800" },
                { rotulo: "Vencido", valor: resumo.totalVencido, tom: "text-red-700" },
                { rotulo: texto.baixado, valor: resumo.totalPago, tom: "text-emerald-700" },
              ].map((cartao) => (
                <div key={cartao.rotulo} className="flex flex-col gap-1 bg-superficie px-5 py-4">
                  <span className="text-xs font-semibold tracking-wide text-slate-500 uppercase">
                    {cartao.rotulo}
                  </span>
                  <span className={`numeros-tabulares text-2xl font-semibold ${cartao.tom}`}>
                    {formatarValor(cartao.valor)}
                  </span>
                </div>
              ))}
            </div>
          )}

          <section
            aria-label="Busca e filtros"
            className="flex flex-col gap-3 rounded-[--radius-cartao] border border-borda bg-superficie p-4 shadow-nivel-1"
          >
            <div className="flex flex-wrap items-end gap-3">
              <div className="relative min-w-0 basis-full sm:flex-1 sm:basis-auto">
                <IconeDeBusca className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-slate-400" />
                <input
                  type="search"
                  value={textoDaBusca}
                  onChange={(evento) => definirRascunho(evento.target.value)}
                  placeholder={texto.busca}
                  aria-label="Buscar lançamentos"
                  className="min-h-11 w-full rounded-[--radius-controle] border border-borda-forte bg-superficie py-2.5 pr-3 pl-9 placeholder:text-slate-400 focus:border-marca-500"
                />
              </div>

              <div className="w-full sm:w-40">
                <Entrada
                  rotulo="Vence de"
                  type="date"
                  value={vencimentoDe}
                  onChange={(evento) => gravar({ vencimentoDe: evento.target.value, pagina: null })}
                />
              </div>
              <div className="w-full sm:w-40">
                <Entrada
                  rotulo="Até"
                  type="date"
                  value={vencimentoAte}
                  onChange={(evento) => gravar({ vencimentoAte: evento.target.value, pagina: null })}
                />
              </div>

              {/* Sem plano de contas, os filtros nem aparecem: não haveria o que escolher. */}
              {categoriasDaNatureza.length > 0 && (
                <div className="w-full sm:w-56">
                  <Selecao
                    rotulo="Categoria"
                    value={categoriaFiltro}
                    onChange={(evento) => gravar({ categoria: evento.target.value, pagina: null })}
                  >
                    <option value="">Todas</option>
                    <option value="sem">Sem categoria</option>
                    {categoriasDaNatureza.map((categoria) => (
                      <option key={categoria.id} value={categoria.id}>
                        {categoria.caminho}
                      </option>
                    ))}
                  </Selecao>
                </div>
              )}

              {centrosDoPlano.length > 0 && (
                <div className="w-full sm:w-48">
                  <Selecao
                    rotulo="Centro de custo"
                    value={centroFiltro}
                    onChange={(evento) => gravar({ centro: evento.target.value, pagina: null })}
                  >
                    <option value="">Todos</option>
                    <option value="sem">Sem centro de custo</option>
                    {centrosDoPlano.map((centro) => (
                      <option key={centro.id} value={centro.id}>
                        {centro.nome}
                      </option>
                    ))}
                  </Selecao>
                </div>
              )}
            </div>

            <div className="flex flex-wrap items-center gap-2">
              {([
                ["", "Todos"],
                ["Aberto", "Em aberto"],
                ["Pago", "Pagos"],
                ["Cancelado", "Cancelados"],
                ["Renegociado", "Renegociados"],
              ] as const).map(([valor, rotulo]) => (
                <button
                  key={rotulo}
                  type="button"
                  /* A página sete de uma situação não é a página sete de outra. */
                  onClick={() => gravar({ situacao: valor, pagina: null })}
                  aria-pressed={situacao === valor}
                  className={
                    "inline-flex min-h-9 items-center rounded-full px-3.5 text-sm font-medium transition-colors " +
                    (situacao === valor
                      ? "bg-marca-600 text-white"
                      : "border border-borda-forte bg-superficie text-slate-700 hover:bg-slate-50")
                  }
                >
                  {rotulo}
                </button>
              ))}
            </div>
          </section>

          {lancamentos.isPending && <p className="text-slate-600">Carregando os lançamentos…</p>}

          {lancamentos.isError && (
            <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
              {lancamentos.error.message}
            </p>
          )}

          {resumo && itens.length === 0 && (
            <div className="rounded-[--radius-cartao] border border-dashed border-borda-forte bg-superficie px-6 py-12 text-center">
              <p className="font-medium text-slate-700">{texto.vazio}</p>
              <p className="mt-1 text-slate-600">{texto.comoEntra}</p>
            </div>
          )}

          {resumo && itens.length > 0 && (
            <>
              {/*
                No celular, cartão; da largura média para cima, tabela. A tabela
                tem sete colunas e a última abre formulário dentro da célula:
                rolando de lado, o valor e o vencimento, que é o que se confere
                antes de confirmar, ficariam para trás.
              */}
              <ul className="flex flex-col gap-3 md:hidden">
                {itens.map((item) => {
                  const estado = estadoDe(item, hoje);
                  const marcada = selecao.marcadas.has(item.id);

                  return (
                    <li key={item.id}>
                      <article
                        className={
                          "rounded-[--radius-cartao] border p-4 shadow-nivel-1 " +
                          (marcada ? "border-marca-400 bg-marca-50" : "border-borda bg-superficie")
                        }
                      >
                        <div className="flex items-start gap-3">
                          {item.situacao === "Aberto" && (
                            <input
                              type="checkbox"
                              checked={marcada}
                              onChange={() => selecao.alternar(item.id)}
                              aria-label={`Selecionar ${item.nomeDaPessoa}, ${item.descricao}`}
                              className="mt-1 size-5 shrink-0 rounded border-borda-forte accent-marca-600"
                            />
                          )}

                          <div className="min-w-0 flex-1">
                            <div className="flex flex-wrap items-center gap-2">
                              <span className="numeros-tabulares text-slate-600">{item.codigoDaPessoa}</span>
                              <span className="font-semibold text-slate-800">{item.nomeDaPessoa}</span>
                              <Tarja estado={estado} />
                            </div>

                            <p className="mt-1 text-slate-600">
                              {item.descricao}
                              {rotuloDaParcela(item)}
                            </p>
                            {classificacaoDe(item) && (
                              <p className="text-xs text-slate-500">{classificacaoDe(item)}</p>
                            )}

                            <dl className="mt-2 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
                              <dt className="text-xs tracking-wide text-slate-400 uppercase">Competência</dt>
                              <dd className="text-slate-700">
                                {formatarCompetencia(item.competenciaAno, item.competenciaMes)}
                              </dd>

                              <dt className="text-xs tracking-wide text-slate-400 uppercase">Vence</dt>
                              <dd className={`numeros-tabulares ${corDoVencimento(item, hoje)}`}>
                                {formatarData(item.vencimento)}
                              </dd>

                              <dt className="text-xs tracking-wide text-slate-400 uppercase">Valor</dt>
                              <dd className="numeros-tabulares font-medium text-slate-800">
                                {formatarValor(item.valor)}
                                {/* != null cobre nulo e ausente: o contrato admite os dois. */}
                                {item.valorPago != null && item.valorPago !== item.valor && (
                                  <span className="block font-normal text-emerald-700">
                                    {textos[item.natureza].baixadoNaFrase} {formatarValor(item.valorPago)}
                                  </span>
                                )}
                                {acrescimosDe(item) && (
                                  <span className="block text-xs font-normal text-slate-600">{acrescimosDe(item)}</span>
                                )}
                              </dd>

                              {item.pagoEm && (
                                <>
                                  <dt className="text-xs tracking-wide text-slate-400 uppercase">Baixado</dt>
                                  <dd className="numeros-tabulares text-slate-700">{detalheDaBaixa(item)}</dd>
                                </>
                              )}

                              {item.motivoDoCancelamento && (
                                <>
                                  <dt className="text-xs tracking-wide text-slate-400 uppercase">Motivo</dt>
                                  <dd className="min-w-0 text-slate-600">{item.motivoDoCancelamento}</dd>
                                </>
                              )}
                            </dl>
                          </div>
                        </div>

                        <div className="mt-3 border-t border-borda pt-3">{acoes(item)}</div>
                      </article>
                    </li>
                  );
                })}
              </ul>

              <div className="hidden overflow-x-auto rounded-[--radius-cartao] border border-borda bg-superficie shadow-nivel-1 md:block">
                <table className="w-full min-w-3xl border-collapse text-left">
                  <thead>
                    <tr className="border-b border-borda bg-slate-50/60 text-xs tracking-wide text-slate-500 uppercase">
                      <th scope="col" className="w-10 px-4 py-3">
                        <input
                          type="checkbox"
                          checked={todasMarcadas}
                          ref={(caixa) => {
                            if (caixa) caixa.indeterminate = algumaMarcada && !todasMarcadas;
                          }}
                          disabled={selecionaveis.length === 0}
                          onChange={() => selecao.trocarTodas(selecionaveis.map((item) => item.id))}
                          aria-label="Selecionar os lançamentos em aberto desta página"
                          className="size-4 rounded border-borda-forte accent-marca-600"
                        />
                      </th>
                      <CabecalhoOrdenavel titulo={texto.pessoa} por="Pessoa" ordem={ordem} aoOrdenar={ordenarPor} />
                      <CabecalhoOrdenavel
                        titulo="Competência"
                        por="Competencia"
                        ordem={ordem}
                        aoOrdenar={ordenarPor}
                      />
                      <CabecalhoOrdenavel
                        titulo="Vencimento"
                        por="Vencimento"
                        ordem={ordem}
                        aoOrdenar={ordenarPor}
                      />
                      <CabecalhoOrdenavel
                        titulo="Valor"
                        alinhamento="direita"
                        por="Valor"
                        ordem={ordem}
                        aoOrdenar={ordenarPor}
                      />
                      <CabecalhoOrdenavel titulo="Situação" ordem={ordem} aoOrdenar={ordenarPor} />
                      <th scope="col" className="px-4 py-3 text-right font-semibold">
                        <span className="sr-only">Ações</span>
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {itens.map((item) => {
                      const marcada = selecao.marcadas.has(item.id);

                      return (
                        <tr
                          key={item.id}
                          className={
                            "border-b border-borda align-top transition-colors last:border-0 " +
                            (marcada ? "bg-marca-50" : "hover:bg-slate-50")
                          }
                        >
                          <td className="px-4 py-3">
                            {item.situacao === "Aberto" && (
                              <input
                                type="checkbox"
                                checked={marcada}
                                onChange={() => selecao.alternar(item.id)}
                                aria-label={`Selecionar ${item.nomeDaPessoa}, ${item.descricao}`}
                                className="size-4 rounded border-borda-forte accent-marca-600"
                              />
                            )}
                          </td>
                          <td className="px-4 py-3">
                            <span className="numeros-tabulares text-slate-600">{item.codigoDaPessoa}</span>{" "}
                            <span className="text-slate-800">{item.nomeDaPessoa}</span>
                            <span className="block text-slate-600">
                              {item.descricao}
                              {rotuloDaParcela(item)}
                            </span>
                            {classificacaoDe(item) && (
                              <span className="block text-xs text-slate-500">{classificacaoDe(item)}</span>
                            )}
                          </td>
                          <td className="px-4 py-3 text-slate-700">
                            {formatarCompetencia(item.competenciaAno, item.competenciaMes)}
                          </td>
                          <td className={`numeros-tabulares px-4 py-3 ${corDoVencimento(item, hoje)}`}>
                            {formatarData(item.vencimento)}
                          </td>
                          <td className="numeros-tabulares px-4 py-3 text-right font-medium text-slate-800">
                            {formatarValor(item.valor)}
                            {/* != null cobre nulo e ausente: o contrato admite os dois. */}
                            {item.valorPago != null && item.valorPago !== item.valor && (
                              <span className="block text-emerald-700">
                                {textos[item.natureza].baixadoNaFrase} {formatarValor(item.valorPago)}
                              </span>
                            )}
                            {acrescimosDe(item) && (
                              <span className="block text-xs font-normal text-slate-600">{acrescimosDe(item)}</span>
                            )}
                          </td>
                          <td className="px-4 py-3">
                            <Tarja estado={estadoDe(item, hoje)} />
                            {item.motivoDoCancelamento && (
                              <span className="mt-1 block max-w-48 text-xs text-slate-600">
                                {item.motivoDoCancelamento}
                              </span>
                            )}
                            {item.pagoEm && (
                              <span className="mt-1 block text-xs text-slate-600">{detalheDaBaixa(item)}</span>
                            )}
                          </td>
                          <td className="px-4 py-3 text-right">{acoes(item)}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>

              <Paginacao
                pagina={resumo.pagina}
                tamanho={resumo.tamanho}
                total={resumo.total}
                aoMudar={definirPagina}
                substantivo={{ singular: "lançamento", plural: "lançamentos" }}
              />
            </>
          )}

          <BarraDeSelecao
            quantidade={selecao.quantidade}
            substantivo={{ singular: "lançamento", plural: "lançamentos" }}
            aoLimpar={selecao.limpar}
          >
            <Botao type="button" onClick={() => definirBaixandoEmLote(true)} className="px-3 py-1.5">
              {selecao.quantidade === 1 ? "Baixar" : `Baixar ${selecao.quantidade}`}
            </Botao>
          </BarraDeSelecao>
        </div>
      </div>

      {/* A chave é a natureza: trocar de aba começa um lançamento do zero. */}
      <GavetaDeLancamento key={natureza} natureza={natureza} aberta={lancando} aoFechar={() => definirLancando(false)} />

      <GavetaDeClassificacao
        key={classificando?.id ?? "nenhum"}
        lancamento={classificando}
        aoFechar={() => definirClassificando(null)}
      />

      <GavetaDeHistorico
        key={historicoAberto?.id ?? "nenhum"}
        alvo={historicoAberto ? { tipo: "lancamento", id: historicoAberto.id } : null}
        titulo="Histórico"
        descricao={historicoAberto ? `${historicoAberto.nomeDaPessoa} · ${historicoAberto.descricao}` : undefined}
        aoFechar={() => definirHistoricoAberto(null)}
      />

      <GavetaDeRenegociacao
        key={renegociando?.id ?? "nenhum"}
        titulo={renegociando}
        aoFechar={() => definirRenegociando(null)}
      />

      <BaixaEmLote
        natureza={natureza}
        selecionados={selecionados}
        aberta={baixandoEmLote}
        aoFechar={() => definirBaixandoEmLote(false)}
        aoConcluir={() => {
          definirBaixandoEmLote(false);
          selecao.limpar();
        }}
      />

      <VoltarAoTopo />
    </>
  );
}
