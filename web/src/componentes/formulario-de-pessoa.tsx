"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/api/cliente";
import { AreaDeTexto, Botao, Entrada, EntradaMascarada, Selecao } from "@/componentes/controles";
import type { components } from "@/api/esquema";
import { apenasDigitos } from "@/lib/formato";
import { mascararCep, mascararCnpj, mascararCpf, mascararTelefone } from "@/lib/mascaras";

type DadosDePessoa = components["schemas"]["DadosDePessoa"];
type Papel = components["schemas"]["Papel"];
type RegimeTributario = components["schemas"]["RegimeTributario"];

/*
 * Os quatro papéis, na ordem em que o escritório pensa neles. Cliente primeiro
 * porque é o que quase todo cadastro vai receber.
 */
const papeis: Record<Papel, string> = {
  Cliente: "Cliente",
  Fornecedor: "Fornecedor",
  Vendedor: "Vendedor",
  Colaborador: "Colaborador",
};

const regimes: Record<RegimeTributario, string> = {
  Mei: "MEI",
  SimplesNacional: "Simples Nacional",
  LucroPresumido: "Lucro Presumido",
  LucroReal: "Lucro Real",
  TerceiroSetor: "Terceiro Setor",
  PessoaFisica: "Pessoa Física",
};
type Problema = components["schemas"]["Problema"];

const UFS = [
  "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS",
  "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC",
  "SP", "SE", "TO",
];

/**
 * A frase certa para cada jeito de a consulta não dar certo.
 *
 * São três, e a diferença importa. "Não existe" pede para conferir o que foi
 * digitado. "Espere um pouco" é o caso comum de API pública e gratuita, e quem
 * ouvir isso tenta de novo daqui a pouco em vez de desistir. "Fora do ar" pede
 * para seguir sem o atalho. Dizer a frase errada manda a pessoa procurar erro
 * onde não há, ou abandonar um atalho que voltaria em dez segundos.
 */
function recadoDaFalha(status: number, oQue: string, oQuePreencher: string): string {
  if (status === 404) return `Não encontramos ${oQue}. Confira o número digitado.`;

  if (status === 429) {
    return "O serviço de consulta está ocupado. Tente de novo em alguns segundos, " +
      `ou preencha ${oQuePreencher} à mão.`;
  }

  return `Não deu para consultar ${oQue} agora. Preencha ${oQuePreencher} à mão.`;
}

const vazia: DadosDePessoa = {
  tipo: "Juridica",
  regimeTributario: "SimplesNacional",
  responsavel: "",
  papeis: [],
  nome: "",
  nomeFantasia: "",
  documento: "",
  inscricaoEstadual: "",
  inscricaoMunicipal: "",
  email: "",
  telefone: "",
  celular: "",
  endereco: {
    cep: "",
    logradouro: "",
    numero: "",
    complemento: "",
    bairro: "",
    cidade: "",
    uf: "",
  },
  observacoes: "",
  ativo: true,
};

/**
 * Reconhece a resposta 422 da API.
 *
 * A validação de verdade mora no servidor (decisão Q20): o front pode
 * conferir o que quiser por experiência, mas quem diz o que entra no banco é a
 * API. Então o formulário não duplica regra — ele sabe **ler** o que a API
 * respondeu e colocar cada problema no campo de onde ele veio.
 */
function problemasDaResposta(erro: unknown): Problema[] {
  if (erro && typeof erro === "object" && "problemas" in erro && Array.isArray(erro.problemas)) {
    return erro.problemas as Problema[];
  }
  return [];
}

export function FormularioDePessoa({ id }: { id?: string }) {
  const navegacao = useRouter();
  const clienteDeConsultas = useQueryClient();
  const editando = Boolean(id);

  const [dados, definirDados] = useState<DadosDePessoa>(vazia);
  const [problemas, definirProblemas] = useState<Problema[]>([]);
  const [procurandoCep, definirProcurandoCep] = useState(false);
  const [avisoDoCep, definirAvisoDoCep] = useState<string | null>(null);
  const [procurandoCnpj, definirProcurandoCnpj] = useState(false);
  const [avisoDoCnpj, definirAvisoDoCnpj] = useState<string | null>(null);

  /*
   * Inativar pede o segundo clique; reativar não.
   *
   * A assimetria é de propósito. Inativar tira a pessoa de circulação, e um
   * clique de mira errada faria isso sem aviso — o botão vira "Confirmar?" e
   * volta ao normal em cinco segundos. Reativar só devolve o que já existia, e
   * quem errar desfaz com um clique. Pedir confirmação nos dois lados
   * ensinaria a clicar duas vezes sem ler, que é como confirmação vira ruído.
   */
  const [confirmando, definirConfirmando] = useState(false);

  /* O motivo da recusa fica até a próxima tentativa: ele diz qual contrato
     encerrar, e quem for conferir isso volta precisando dele ainda ali. */
  const [impedimentos, definirImpedimentos] = useState<string[]>([]);

  useEffect(() => {
    if (!confirmando) return;
    const relogio = setTimeout(() => definirConfirmando(false), 5000);
    return () => clearTimeout(relogio);
  }, [confirmando]);

  const existente = useQuery({
    queryKey: ["pessoa", id],
    enabled: editando,
    queryFn: async () => {
      const { data, error } = await api.GET("/pessoas/{id}", {
        params: { path: { id: id! } },
      });
      if (error || !data) throw new Error("Este cadastro não foi encontrado.");
      return data;
    },
  });

  /*
   * O estado do formulário só é preenchido quando o cadastro chega. Iniciar o
   * `useState` com o dado da consulta não funcionaria: no primeiro render ele
   * ainda não existe, e o valor inicial de um `useState` fica congelado.
   */
  useEffect(() => {
    if (!existente.data) return;
    const { id: _id, criadoEm: _criadoEm, ...resto } = existente.data;
    definirDados(resto);
  }, [existente.data]);

  const salvar = useMutation({
    mutationFn: async (corpo: DadosDePessoa) => {
      const resposta = editando
        ? await api.PUT("/pessoas/{id}", { params: { path: { id: id! } }, body: corpo })
        : await api.POST("/pessoas", { body: corpo });

      if (resposta.error) throw resposta.error;
      return resposta.data;
    },
    onSuccess: () => {
      definirProblemas([]);
      clienteDeConsultas.invalidateQueries({ queryKey: ["pessoas"] });
      navegacao.push("/pessoas");
    },
    onError: (erro) => definirProblemas(problemasDaResposta(erro)),
  });

  /*
   * Inativar e reativar ficam aqui, e não na listagem. A decisão precisa do
   * cadastro aberto: quantos contratos a pessoa tem, o que ela ainda deve. Na
   * linha da tabela o clique vinha antes da informação, e o desfecho mais
   * provável era descobrir que não podia depois de já ter clicado.
   *
   * Nenhum dos dois passa pelo <b>Salvar</b>. São mudanças de situação, não de
   * cadastro: exigir salvar junto misturaria "quero esta pessoa fora de
   * circulação" com "corrigi o telefone dela", e um rascunho pela metade
   * impediria a inativação sem ter nada a ver com ela.
   */
  const mudarSituacao = useMutation({
    mutationFn: async (paraAtivo: boolean) => {
      const resposta = paraAtivo
        ? await api.POST("/pessoas/{id}/reativar", { params: { path: { id: id! } } })
        : await api.DELETE("/pessoas/{id}", { params: { path: { id: id! } } });

      if (resposta.error) throw resposta.error;
      return paraAtivo;
    },
    onMutate: () => definirImpedimentos([]),
    onSuccess: (paraAtivo) => {
      definirConfirmando(false);
      alterar("ativo", paraAtivo);

      /*
       * A listagem recarrega; este cadastro, não. Invalidar a consulta daqui
       * traria o cadastro do servidor de volta por cima do formulário, e
       * apagaria o que estivesse digitado e ainda não salvo — alguém que
       * corrigiu o telefone e inativou na mesma visita perderia a correção sem
       * saber. O único campo que mudou no servidor é este, e ele já está certo
       * aqui.
       */
      clienteDeConsultas.invalidateQueries({ queryKey: ["pessoas"] });
    },
    onError: (erro: unknown) => {
      /* Volta a "Inativar": insistir no mesmo clique não muda nada. */
      definirConfirmando(false);

      const recusas = problemasDaResposta(erro);
      definirImpedimentos(
        recusas.length > 0
          ? recusas.map((problema) => `${problema.descricao} ${problema.sugestao}`)
          : ["Não foi possível mudar a situação deste cadastro. Tente de novo em instantes."],
      );
    },
  });

  const erroDe = (campo: string) => problemas.find((p) => p.campo === campo)?.descricao;
  const sugestaoDe = (campo: string) => problemas.find((p) => p.campo === campo)?.sugestao;

  const ehFisica = dados.tipo === "Fisica";

  function alterar<C extends keyof DadosDePessoa>(campo: C, valor: DadosDePessoa[C]) {
    definirDados((atual) => ({ ...atual, [campo]: valor }));
  }

  /**
   * Trocar o tipo apara o documento para o tamanho do novo.
   *
   * Sem isto, um CNPJ digitado antes de trocar para Física continuaria com
   * quatorze dígitos no estado enquanto a tela mostraria onze: o campo diria
   * uma coisa e o envio faria outra. Divergência entre o que se vê e o que se
   * manda é o tipo de erro que ninguém procura, porque a tela parece certa.
   */
  function alterarTipo(tipo: DadosDePessoa["tipo"]) {
    definirDados((atual) => ({
      ...atual,
      tipo,
      documento: apenasDigitos(atual.documento ?? "").slice(0, tipo === "Fisica" ? 11 : 14),
    }));
  }

  /**
   * Procura o endereço assim que o CEP fica completo.
   *
   * <b>É atalho, e atalho não pode atrapalhar.</b> Nada aqui bloqueia o
   * formulário, e falhar não impede salvar: quem quiser digita o endereço como
   * sempre digitou. Por isso também a busca sai daqui, do que a pessoa digitou,
   * e não de um efeito sobre o valor — assim abrir um cadastro existente não
   * dispara consulta nenhuma, nem sobrescreve o endereço que já estava lá.
   */
  async function procurarPeloCep(cep: string) {
    if (cep.length !== 8) {
      definirAvisoDoCep(null);
      return;
    }

    definirProcurandoCep(true);
    definirAvisoDoCep(null);

    const { data, response } = await api.GET("/consultas/cep/{cep}", {
      params: { path: { cep } },
    });

    definirProcurandoCep(false);

    if (data) {
      definirDados((atual) => ({
        ...atual,
        endereco: {
          ...(atual.endereco ?? vazia.endereco!),
          cep,
          logradouro: data.logradouro,
          bairro: data.bairro,
          cidade: data.cidade,
          uf: data.uf,
        },
      }));
      return;
    }

    /*
     * Duas falhas, duas frases. "Não existe" pede para conferir o que foi
     * digitado; "não deu para perguntar" pede para seguir sem o atalho. Dizer a
     * segunda coisa na primeira situação manda a pessoa procurar erro onde não
     * há.
     */
    definirAvisoDoCep(recadoDaFalha(response.status, "o CEP", "o endereço"));
  }

  /**
   * Procura a empresa quando o CNPJ fica completo.
   *
   * Preenche razão social, nome fantasia e o endereço inteiro. Sobrescreve o que
   * estiver lá: quem acabou de digitar o CNPJ está dizendo de que empresa se
   * trata, e o que havia antes era de outra.
   *
   * <b>Não preenche o número nem o complemento sem querer:</b> eles vêm do
   * cadastro da Receita e costumam estar desatualizados, mas é o que existe, e
   * apagar o que veio seria pior do que trazer algo a conferir.
   */
  async function procurarPeloCnpj(cnpj: string) {
    if (cnpj.length !== 14) {
      definirAvisoDoCnpj(null);
      return;
    }

    definirProcurandoCnpj(true);
    definirAvisoDoCnpj(null);

    const { data, response } = await api.GET("/consultas/cnpj/{cnpj}", {
      params: { path: { cnpj } },
    });

    definirProcurandoCnpj(false);

    if (data) {
      definirDados((atual) => ({
        ...atual,
        nome: data.razaoSocial,
        nomeFantasia: data.nomeFantasia,
        endereco: {
          ...(atual.endereco ?? vazia.endereco!),
          cep: data.cep,
          logradouro: data.logradouro,
          numero: data.numero,
          complemento: data.complemento,
          bairro: data.bairro,
          cidade: data.cidade,
          uf: data.uf,
        },
      }));

      /*
       * Empresa que não está ativa não impede nada — o escritório às vezes
       * atende justamente quem precisa regularizar. Mas precisa aparecer, e
       * antes de salvar: descobrir depois custa retrabalho de verdade.
       */
      definirAvisoDoCnpj(
        data.situacao && data.situacao !== "ATIVA"
          ? `Atenção: esta empresa consta como ${data.situacao} na Receita.`
          : null,
      );
      return;
    }

    definirAvisoDoCnpj(recadoDaFalha(response.status, "o CNPJ", "os dados da empresa"));
  }

  function alterarEndereco(campo: keyof NonNullable<DadosDePessoa["endereco"]>, valor: string) {
    definirDados((atual) => ({
      ...atual,
      endereco: { ...(atual.endereco ?? vazia.endereco!), [campo]: valor },
    }));
  }

  if (editando && existente.isPending) {
    return <p className="p-6 text-slate-500">Carregando o cadastro…</p>;
  }

  if (editando && existente.isError) {
    return (
      <div className="p-6">
        <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
          {existente.error.message}
        </p>
        <Link href="/pessoas" className="mt-4 inline-block text-marca-700 hover:underline">
          Voltar para a lista
        </Link>
      </div>
    );
  }

  return (
    <form
      className="flex flex-1 flex-col"
      onSubmit={(evento) => {
        evento.preventDefault();
        salvar.mutate(dados);
      }}
    >
      <header className="flex flex-wrap items-center justify-between gap-4 border-b border-borda bg-superficie px-6 py-4">
        <div>
          <h1 className="flex flex-wrap items-center gap-2 text-xl font-semibold tracking-tight text-marca-950">
            {editando ? dados.nome || "Cadastro" : "Nova pessoa"}

            {editando && !dados.ativo && (
              <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                inativa
              </span>
            )}
          </h1>
          <p className="text-slate-500">
            Só o nome e o documento são obrigatórios. O resto pode entrar depois.
          </p>
        </div>
      </header>

      <div className="flex flex-1 flex-col gap-6 p-6">
        {problemas.length > 0 && (
          <div
            role="alert"
            className="rounded-[--radius-controle] border border-red-200 bg-red-50 px-4 py-3"
          >
            <p className="font-semibold text-red-800">
              {problemas.length === 1
                ? "Há um problema no cadastro."
                : `Há ${problemas.length} problemas no cadastro.`}
            </p>
            <p className="mt-1 text-red-700">Os campos com erro estão marcados abaixo.</p>
          </div>
        )}

        {salvar.isError && problemas.length === 0 && (
          <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-4 py-3 text-red-700">
            Não foi possível salvar. Verifique se a API está no ar e tente de novo.
          </p>
        )}

        <section className="flex flex-col gap-4 rounded-[--radius-cartao] border border-borda bg-superficie p-5">
          <div>
            <h2 className="text-xs font-semibold tracking-wide text-slate-500 uppercase">
              Papéis
            </h2>
            <p className="mt-1 text-slate-500">
              O que esta pessoa é para o escritório. Pode ser mais de um: a gráfica que imprime
              os carnês costuma ser fornecedora e cliente ao mesmo tempo.
            </p>
          </div>

          <div className="flex flex-wrap gap-x-6 gap-y-3">
            {(Object.entries(papeis) as [Papel, string][]).map(([valor, rotulo]) => (
              <label key={valor} className="flex items-center gap-2 text-slate-700">
                <input
                  type="checkbox"
                  checked={(dados.papeis ?? []).includes(valor)}
                  onChange={(evento) =>
                    alterar(
                      "papeis",
                      evento.target.checked
                        ? [...(dados.papeis ?? []), valor]
                        : (dados.papeis ?? []).filter((atual) => atual !== valor),
                    )
                  }
                  className="size-4 rounded border-borda-forte accent-marca-600"
                />
                {rotulo}
              </label>
            ))}
          </div>
        </section>

        <section className="flex flex-col gap-4 rounded-[--radius-cartao] border border-borda bg-superficie p-5">
          <h2 className="text-xs font-semibold tracking-wide text-slate-500 uppercase">
            Identificação
          </h2>

          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <Selecao
              rotulo="Tipo"
              value={dados.tipo}
              onChange={(evento) => alterarTipo(evento.target.value as DadosDePessoa["tipo"])}
            >
              <option value="Juridica">Jurídica</option>
              <option value="Fisica">Física</option>
            </Selecao>

            {/*
              A máscara vem do tipo escolhido, e não do tamanho do que foi
              digitado. Adivinhar pelo tamanho faria a pontuação pular de CPF
              para CNPJ no décimo segundo dígito, no meio da digitação.
            */}
            <EntradaMascarada
              rotulo={ehFisica ? "CPF" : "CNPJ"}
              digitos={dados.documento ?? ""}
              mascara={ehFisica ? mascararCpf : mascararCnpj}
              aoMudar={(digitos) => {
                alterar("documento", digitos);
                /* Só para empresa: não existe consulta pública de CPF, e nem
                   deveria existir. */
                if (!ehFisica) void procurarPeloCnpj(digitos);
              }}
              erro={erroDe("documento")}
              ajuda={
                ehFisica
                  ? sugestaoDe("documento")
                  : procurandoCnpj
                    ? "Procurando na Receita…"
                    : (avisoDoCnpj ??
                       sugestaoDe("documento") ??
                       "Preenche razão social e endereço sozinho.")
              }
            />

            <div className="sm:col-span-2">
              <Entrada
                rotulo={ehFisica ? "Nome completo" : "Razão social"}
                value={dados.nome ?? ""}
                onChange={(evento) => alterar("nome", evento.target.value)}
                erro={erroDe("nome")}
                ajuda={sugestaoDe("nome")}
              />
            </div>

            {!ehFisica && (
              <>
                <div className="sm:col-span-2">
                  <Entrada
                    rotulo="Nome fantasia"
                    value={dados.nomeFantasia ?? ""}
                    onChange={(evento) => alterar("nomeFantasia", evento.target.value)}
                  />
                </div>

                <Entrada
                  rotulo="Inscrição estadual"
                  value={dados.inscricaoEstadual ?? ""}
                  onChange={(evento) => alterar("inscricaoEstadual", evento.target.value)}
                  erro={erroDe("inscricaoEstadual")}
                  ajuda={sugestaoDe("inscricaoEstadual") ?? "Ou ISENTO."}
                />

                <Entrada
                  rotulo="Inscrição municipal"
                  value={dados.inscricaoMunicipal ?? ""}
                  onChange={(evento) => alterar("inscricaoMunicipal", evento.target.value)}
                />
              </>
            )}

            {/*
              Regime e responsável ficam aqui, e não num bloco de cliente, porque
              não são do papel: o regime é o da empresa na Receita, cliente ou
              não, e o responsável é quem no escritório cuida dela.
            */}
            <Selecao
              rotulo="Regime tributário"
              value={dados.regimeTributario}
              onChange={(evento) =>
                alterar("regimeTributario", evento.target.value as RegimeTributario)
              }
            >
              {Object.entries(regimes).map(([valor, rotulo]) => (
                <option key={valor} value={valor}>
                  {rotulo}
                </option>
              ))}
            </Selecao>

            <Entrada
              rotulo="Responsável"
              value={dados.responsavel ?? ""}
              onChange={(evento) => alterar("responsavel", evento.target.value)}
              ajuda="Quem cuida desta pessoa no escritório."
            />
          </div>
        </section>

        <section className="flex flex-col gap-4 rounded-[--radius-cartao] border border-borda bg-superficie p-5">
          <h2 className="text-xs font-semibold tracking-wide text-slate-500 uppercase">Contato</h2>

          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <div className="sm:col-span-2">
              <Entrada
                rotulo="E-mail"
                type="email"
                value={dados.email ?? ""}
                onChange={(evento) => alterar("email", evento.target.value)}
                erro={erroDe("email")}
                ajuda={sugestaoDe("email")}
              />
            </div>

            <EntradaMascarada
              rotulo="Telefone"
              inputMode="tel"
              digitos={apenasDigitos(dados.telefone ?? "")}
              mascara={mascararTelefone}
              aoMudar={(digitos) => alterar("telefone", digitos)}
              erro={erroDe("telefone")}
              ajuda={sugestaoDe("telefone")}
            />

            <EntradaMascarada
              rotulo="Celular"
              inputMode="tel"
              digitos={apenasDigitos(dados.celular ?? "")}
              mascara={mascararTelefone}
              aoMudar={(digitos) => alterar("celular", digitos)}
              erro={erroDe("celular")}
              ajuda={sugestaoDe("celular")}
            />
          </div>
        </section>

        <section className="flex flex-col gap-4 rounded-[--radius-cartao] border border-borda bg-superficie p-5">
          <h2 className="text-xs font-semibold tracking-wide text-slate-500 uppercase">Endereço</h2>

          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-6">
            <EntradaMascarada
              rotulo="CEP"
              digitos={dados.endereco?.cep ?? ""}
              mascara={mascararCep}
              aoMudar={(digitos) => {
                alterarEndereco("cep", digitos);
                void procurarPeloCep(digitos);
              }}
              erro={erroDe("endereco.cep")}
              ajuda={
                procurandoCep
                  ? "Procurando o endereço…"
                  : (avisoDoCep ?? sugestaoDe("endereco.cep") ?? "Preenche o endereço sozinho.")
              }
            />

            <div className="lg:col-span-3">
              <Entrada
                rotulo="Logradouro"
                value={dados.endereco?.logradouro ?? ""}
                onChange={(evento) => alterarEndereco("logradouro", evento.target.value)}
              />
            </div>

            <Entrada
              rotulo="Número"
              value={dados.endereco?.numero ?? ""}
              onChange={(evento) => alterarEndereco("numero", evento.target.value)}
            />

            <Entrada
              rotulo="Complemento"
              value={dados.endereco?.complemento ?? ""}
              onChange={(evento) => alterarEndereco("complemento", evento.target.value)}
            />

            <div className="lg:col-span-2">
              <Entrada
                rotulo="Bairro"
                value={dados.endereco?.bairro ?? ""}
                onChange={(evento) => alterarEndereco("bairro", evento.target.value)}
              />
            </div>

            <div className="lg:col-span-3">
              <Entrada
                rotulo="Cidade"
                value={dados.endereco?.cidade ?? ""}
                onChange={(evento) => alterarEndereco("cidade", evento.target.value)}
              />
            </div>

            <Selecao
              rotulo="UF"
              value={dados.endereco?.uf ?? ""}
              onChange={(evento) => alterarEndereco("uf", evento.target.value)}
              erro={erroDe("endereco.uf")}
            >
              <option value="">—</option>
              {UFS.map((uf) => (
                <option key={uf} value={uf}>
                  {uf}
                </option>
              ))}
            </Selecao>
          </div>
        </section>

        <section className="flex flex-col gap-4 rounded-[--radius-cartao] border border-borda bg-superficie p-5">
          <AreaDeTexto
            rotulo="Observações"
            value={dados.observacoes ?? ""}
            onChange={(evento) => alterar("observacoes", evento.target.value)}
          />
        </section>
      </div>

      {/*
        O motivo da recusa encosta no botão que a provocou. No topo da página
        ele estaria fora da tela: o rodapé é fixo, e quem clicou está olhando
        para cá.
      */}
      <div className="sticky bottom-0 border-t border-borda bg-superficie">
        {impedimentos.length > 0 && (
          <div
            role="alert"
            className="border-b border-amber-200 bg-amber-50 px-6 py-3 text-amber-900"
          >
            <p className="font-medium">Este cadastro ainda está em uso.</p>
            <ul className="mt-1 list-disc space-y-1 pl-5">
              {impedimentos.map((motivo) => (
                <li key={motivo}>{motivo}</li>
              ))}
            </ul>
          </div>
        )}

        <footer className="flex flex-wrap items-center justify-between gap-3 px-6 py-3">
          {/* Fica sozinho à esquerda, longe do Salvar: é a ação que se clica sem querer. */}
          {editando ? (
            dados.ativo ? (
              <Botao
                aparencia={confirmando ? "perigo" : "secundario"}
                type="button"
                disabled={mudarSituacao.isPending}
                onClick={() => (confirmando ? mudarSituacao.mutate(false) : definirConfirmando(true))}
              >
                {confirmando ? "Confirmar?" : "Inativar cadastro"}
              </Botao>
            ) : (
              <Botao
                aparencia="secundario"
                type="button"
                disabled={mudarSituacao.isPending}
                onClick={() => mudarSituacao.mutate(true)}
              >
                Reativar cadastro
              </Botao>
            )
          ) : (
            <span />
          )}

          <div className="flex items-center gap-3">
            <Link
              href="/pessoas"
              className="rounded-[--radius-controle] border border-borda-forte px-4 py-2 font-semibold text-slate-700 transition-colors hover:bg-slate-50"
            >
              Cancelar
            </Link>

            <Botao type="submit" disabled={salvar.isPending}>
              {salvar.isPending ? "Salvando…" : "Salvar"}
            </Botao>
          </div>
        </footer>
      </div>
    </form>
  );
}
