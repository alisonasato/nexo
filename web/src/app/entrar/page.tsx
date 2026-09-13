"use client";

import Image from "next/image";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";

import { Campo } from "@/componentes/controles";
import { useEntrar, useSessao } from "@/sessao/usar-sessao";
import montanhas from "./montanhas.jpg";

/**
 * A tela de entrada, num cartão dividido entre formulário e fotografia.
 *
 * <b>O outro lado do cartão é recuperar acesso, e não cadastro.</b> O desenho
 * de referência desliza entre entrar e criar conta, e o Nexo não tem criação
 * de conta: um escritório novo é provisionado, não se cadastra. Um formulário
 * de cadastro aqui enviaria para lugar nenhum. Recuperar acesso é a pergunta
 * que alguém nesta tela de fato tem, e a resposta existe — só não é por aqui.
 *
 * <b>Sem botões de login social</b>, pelo mesmo motivo: a autenticação é
 * e-mail e senha, e um botão do Google que não entra é pior que botão nenhum.
 */

type Vista = "entrar" | "recuperar";

const juntar = (...partes: (string | false)[]) => partes.filter(Boolean).join(" ");

/*
 * O deslize vale só da largura média para cima, onde o cartão é dividido.
 *
 * As propriedades são nomeadas, e não `transition-all`: esta folha global
 * fixa a duração de `.transition-all` no tempo curto das outras telas, e o
 * deslize precisa dos 650 ms do desenho para não parecer um salto. E quem
 * pediu menos movimento no sistema continua sem movimento nenhum, porque a
 * regra global de movimento reduzido vale para qualquer transição.
 */
const entrando =
  "md:[transition:translate_650ms_ease-in-out,opacity_650ms_ease-in-out,visibility_0s]";
const saindo =
  "md:[transition:translate_650ms_ease-in-out,opacity_650ms_ease-in-out,visibility_0s_linear_650ms]";

/*
 * A visibilidade troca na hora para quem entra, e só no fim para quem sai.
 *
 * Animada junto com o resto, ela deixava o lado que entra escondido no
 * instante em que o foco é movido para ele. Foi o que a verificação mostrou:
 * com o deslize parado no começo, o foco caiu no corpo da página em vez do
 * título. Quem sai precisa continuar visível até o fim, senão some antes de
 * deslizar.
 */
const lado = (ativo: boolean, seAtivo: string, seInativo: string) =>
  `${ativo ? entrando : saindo} ${ativo ? seAtivo : seInativo}`;

const campo =
  "w-full rounded-[--radius-controle] border border-transparent bg-areia px-3.5 py-3 " +
  "text-slate-800 transition-colors placeholder:text-slate-400 " +
  "focus:border-carvao/30 focus:bg-white aria-[invalid=true]:border-red-500";

export default function PaginaDeEntrada() {
  const navegacao = useRouter();
  const sessao = useSessao();
  const entrar = useEntrar();

  const [email, definirEmail] = useState("");
  const [senha, definirSenha] = useState("");
  const [vista, definirVista] = useState<Vista>("entrar");

  const campoDeEmail = useRef<HTMLInputElement>(null);
  const tituloDeRecuperar = useRef<HTMLHeadingElement>(null);
  const trocouDeVista = useRef(false);

  const naEntrada = vista === "entrar";

  /* Quem já entrou não precisa ver esta tela. */
  useEffect(() => {
    if (sessao.data) navegacao.replace("/pessoas");
  }, [sessao.data, navegacao]);

  /*
   * O foco acompanha a troca de lado.
   *
   * Sem isto, quem navega por teclado ficaria com o foco preso num botão que
   * acabou de sumir, e o leitor de tela não anunciaria que o conteúdo mudou.
   * Voltando para entrar, o foco vai direto ao e-mail, que é o que a pessoa
   * veio fazer. Na primeira visita nada acontece aqui: o `autoFocus` do campo
   * já cuida.
   */
  useEffect(() => {
    if (!trocouDeVista.current) return;
    if (vista === "entrar") campoDeEmail.current?.focus();
    else tituloDeRecuperar.current?.focus();
  }, [vista]);

  function trocar(proxima: Vista) {
    trocouDeVista.current = true;
    definirVista(proxima);
  }

  return (
    <main className="flex min-h-dvh items-center justify-center bg-areia px-4 py-10">
      <div className="relative w-full max-w-sm overflow-hidden rounded-3xl border-8 border-white bg-white shadow-cartao-de-entrada md:h-[440px] md:w-[660px] md:max-w-none">
        {/*
          A fotografia. No celular é uma faixa no alto do cartão; da largura
          média para cima ocupa metade dele e desliza para o lado oposto ao do
          conteúdo. É decorativa, então não tem texto alternativo: o que ela
          acompanha está escrito por cima.
        */}
        <div
          className={juntar(
            "relative h-44 overflow-hidden rounded-[18px]",
            "md:absolute md:inset-y-0 md:left-0 md:z-20 md:h-auto md:w-1/2",
            "md:transition-[translate] md:duration-[650ms] md:ease-in-out",
            naEntrada && "md:translate-x-full",
          )}
        >
          <Image
            src={montanhas}
            alt=""
            fill
            placeholder="blur"
            /* É a maior imagem visível ao abrir a tela: sai antes do resto. */
            loading="eager"
            fetchPriority="high"
            /*
             * Largura da imagem recortada, e não da caixa. No cartão ela ocupa
             * uma coluna estreita e alta, e cobrir essa coluna exige uma imagem
             * bem mais larga do que a coluna; pedir só a largura da caixa
             * entregaria uma fotografia borrada.
             */
            sizes="(min-width: 768px) 760px, 100vw"
            className="object-cover"
          />
          {/*
            Véu leve. Medido sobre os pixels que ficam atrás do texto, 40% já
            dá mais de 10:1 de contraste no pior caso, bem acima do mínimo de
            4,5:1; mais escuro que isso só apagava as montanhas.
          */}
          <div className="absolute inset-0 bg-carvao/40" />
        </div>

        {/* Texto sobre a fotografia, do lado de entrar. */}
        <div
          inert={!naEntrada}
          className={juntar(
            "absolute inset-x-0 top-0 z-30 flex h-44 flex-col items-center justify-center gap-2 px-6 text-center text-white",
            "md:inset-x-auto md:bottom-0 md:left-1/2 md:h-auto md:w-1/2 md:gap-4",
            lado(naEntrada, "visible opacity-100", "invisible opacity-0 md:translate-x-full"),
          )}
        >
          <p className="text-xs font-semibold tracking-[0.2em] uppercase opacity-80">Nexo</p>
          <p className="text-2xl font-medium">Olá de novo</p>
          <p className="hidden max-w-60 leading-relaxed opacity-90 md:block">
            Clientes, contratos e o que entra no caixa do escritório, num lugar só.
          </p>
        </div>

        {/* Texto sobre a fotografia, do lado de recuperar. */}
        <div
          inert={naEntrada}
          className={juntar(
            "absolute inset-x-0 top-0 z-30 flex h-44 flex-col items-center justify-center gap-3 px-6 text-center text-white",
            "md:inset-x-auto md:bottom-0 md:left-0 md:h-auto md:w-1/2 md:gap-4",
            lado(!naEntrada, "visible opacity-100", "invisible opacity-0 md:-translate-x-full"),
          )}
        >
          <p className="text-2xl font-medium">Lembrou a senha?</p>
          <p className="hidden max-w-60 leading-relaxed opacity-90 md:block">
            Volte e entre com o e-mail e a senha de sempre.
          </p>
          <button
            type="button"
            onClick={() => trocar("entrar")}
            className="min-h-11 cursor-pointer rounded-full border border-white/90 px-8 text-xs font-semibold tracking-[0.12em] uppercase transition-colors hover:bg-white hover:text-carvao"
          >
            Voltar para entrar
          </button>
        </div>

        {/* Entrar. */}
        <section
          inert={!naEntrada}
          aria-labelledby="titulo-de-entrar"
          className={juntar(
            "flex-col justify-center gap-5 bg-white px-5 pt-7 pb-6",
            "md:absolute md:inset-y-0 md:left-0 md:z-10 md:flex md:w-1/2 md:px-8 md:py-0",
            lado(naEntrada, "flex md:visible md:opacity-100", "hidden md:invisible md:translate-x-full md:opacity-0"),
          )}
        >
          <header className="flex flex-col gap-1 text-center">
            <h1 id="titulo-de-entrar" className="text-[1.375rem] font-medium text-carvao">
              Entrar
            </h1>
            <p className="text-slate-500">Com o e-mail e a senha do escritório.</p>
          </header>

          <form
            className="flex flex-col gap-3.5"
            onSubmit={(evento) => {
              evento.preventDefault();
              entrar.mutate(
                { email, senha },
                { onSuccess: () => navegacao.replace("/pessoas") },
              );
            }}
          >
            {/*
              Rótulo visível, e não só o texto de exemplo do desenho de
              referência: o exemplo some assim que se começa a digitar, e com
              ele a única pista do que o campo pede. Pior ainda com o
              preenchimento automático do navegador, que ocupa os dois campos
              antes de a pessoa ler qualquer coisa.
            */}
            <Campo rotulo="E-mail">
              {({ id, descritoPor, temErro }) => (
                <input
                  ref={campoDeEmail}
                  id={id}
                  type="email"
                  name="email"
                  autoComplete="username"
                  required
                  autoFocus
                  value={email}
                  onChange={(evento) => definirEmail(evento.target.value)}
                  aria-invalid={temErro || undefined}
                  aria-describedby={descritoPor}
                  className={campo}
                />
              )}
            </Campo>

            <Campo rotulo="Senha">
              {({ id, descritoPor, temErro }) => (
                <input
                  id={id}
                  type="password"
                  name="senha"
                  autoComplete="current-password"
                  required
                  value={senha}
                  onChange={(evento) => definirSenha(evento.target.value)}
                  aria-invalid={temErro || undefined}
                  aria-describedby={descritoPor}
                  className={campo}
                />
              )}
            </Campo>

            {entrar.isError && (
              /*
               * `role="alert"` para o leitor de tela anunciar a falha: quem não
               * enxerga a tela precisa saber que a tentativa foi recusada.
               */
              <p role="alert" className="rounded-[--radius-controle] bg-red-50 px-3 py-2 text-red-700">
                {entrar.error.message}
              </p>
            )}

            <button
              type="button"
              onClick={() => trocar("recuperar")}
              className="min-h-11 cursor-pointer self-center px-2 text-slate-500 underline-offset-4 transition-colors hover:text-carvao hover:underline md:min-h-9"
            >
              Esqueci minha senha
            </button>

            <button
              type="submit"
              disabled={entrar.isPending}
              className="min-h-12 w-40 cursor-pointer self-center rounded-full bg-carvao text-xs font-semibold tracking-[0.12em] text-white uppercase transition-colors hover:bg-carvao/90 disabled:cursor-not-allowed disabled:opacity-60"
            >
              {entrar.isPending ? "Entrando…" : "Entrar"}
            </button>
          </form>
        </section>

        {/* Recuperar acesso. */}
        <section
          inert={naEntrada}
          aria-labelledby="titulo-de-recuperar"
          className={juntar(
            "flex-col justify-center gap-4 bg-white px-5 pt-7 pb-6",
            "md:absolute md:inset-y-0 md:left-1/2 md:z-10 md:flex md:w-1/2 md:px-8 md:py-0",
            lado(!naEntrada, "flex md:visible md:opacity-100", "hidden md:invisible md:-translate-x-full md:opacity-0"),
          )}
        >
          <h1
            ref={tituloDeRecuperar}
            id="titulo-de-recuperar"
            tabIndex={-1}
            className="text-center text-[1.375rem] font-medium text-carvao"
          >
            Recuperar acesso
          </h1>

          {/*
            O texto diz a verdade sobre o que existe hoje. Não há "enviar link
            de recuperação", porque o Nexo ainda não manda e-mail, e um botão
            que prometesse isso deixaria alguém esperando uma mensagem que não
            vem. O caminho real é o descrito no IMPLANTACAO.md.
          */}
          <div className="flex flex-col gap-3 leading-relaxed text-slate-600">
            <p>A senha ainda não se recupera por esta tela, porque o Nexo não envia e-mail.</p>
            <p>
              Quem administra a instalação define uma senha nova pelo painel onde o Nexo roda. Por
              segurança, as sessões abertas nesta conta caem junto.
            </p>
          </div>
        </section>
      </div>
    </main>
  );
}
