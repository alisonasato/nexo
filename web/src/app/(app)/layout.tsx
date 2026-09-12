"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";

import { ProvedorDeAvisos } from "@/componentes/avisos";
import { useSair, useSessao } from "@/sessao/usar-sessao";

const menu = [
  { href: "/pessoas", rotulo: "Pessoas" },
  { href: "/contratos", rotulo: "Contratos" },
  { href: "/recebiveis", rotulo: "Recebíveis" },
];

/**
 * A casca das telas autenticadas.
 *
 * A checagem de sessão aqui é de **experiência**, não de segurança: ela evita
 * mostrar uma tela vazia a quem não entrou. Quem protege o dado é a API, que
 * recusa sem cookie válido, e a política de RLS, que recusa sem tenant. Se
 * alguém desligar este JavaScript, não ganha nada além de uma tela em branco.
 *
 * A navegação é barra lateral no desktop e barra superior no celular — nunca
 * escondida. A primeira versão a escondia abaixo de 640px, e com ela sumia o
 * botão de sair: ficar preso numa sessão aberta, num sistema com dinheiro
 * dentro, é defeito, não simplificação.
 */
export default function LayoutDaAplicacao({ children }: { children: ReactNode }) {
  const navegacao = useRouter();
  const caminho = usePathname();
  const sessao = useSessao();
  const sair = useSair();

  useEffect(() => {
    if (!sessao.isPending && sessao.data === null) navegacao.replace("/entrar");
  }, [sessao.isPending, sessao.data, navegacao]);

  if (sessao.isPending) {
    return (
      <div className="flex min-h-dvh items-center justify-center text-slate-600">
        Carregando…
      </div>
    );
  }

  /*
   * Numa variável, e não em `sessao.data` direto: o retorno do useQuery é uma
   * união, e testar a propriedade não estreita o objeto todo — o TypeScript
   * continuaria achando que o valor pode ser nulo lá embaixo.
   */
  const usuario = sessao.data;
  if (!usuario) return null;

  const encerrar = () =>
    sair.mutate(undefined, { onSuccess: () => navegacao.replace("/entrar") });

  const aparencia = (ativo: boolean) =>
    "rounded-[--radius-controle] px-3 py-2 font-medium transition-colors " +
    (ativo ? "bg-marca-800 text-white" : "hover:bg-marca-900");

  return (
    /* Os avisos moram na casca: qualquer tela de dentro pode emitir. */
    <ProvedorDeAvisos>
      <div className="flex min-h-dvh flex-col sm:flex-row">
        <aside className="flex shrink-0 flex-col bg-marca-950 text-marca-100 sm:w-56">
          <div className="flex items-center justify-between gap-4 px-5 py-4 sm:py-5">
            <span className="text-lg font-semibold tracking-tight text-white">Nexo</span>

            {/* No celular, sair fica no topo: é o único lugar que sempre aparece. */}
            <button
              type="button"
              onClick={encerrar}
              className="text-xs font-semibold text-marca-200 underline-offset-2 hover:underline sm:hidden"
            >
              Sair
            </button>
          </div>

          <nav className="flex gap-1 overflow-x-auto px-3 pb-3 sm:flex-1 sm:flex-col sm:overflow-visible sm:pb-0">
            {menu.map((item) => (
              <Link
                key={item.href}
                href={item.href}
                aria-current={caminho.startsWith(item.href) ? "page" : undefined}
                className={aparencia(caminho.startsWith(item.href))}
              >
                {item.rotulo}
              </Link>
            ))}

            {/*
              No desktop a conta e a empresa se alcançam pelo rodapé da barra. No
              celular não existe rodapé, então elas precisam estar aqui — senão
              não haveria como trocar a senha pelo telefone.
            */}
            <Link
              href="/empresa"
              aria-current={caminho.startsWith("/empresa") ? "page" : undefined}
              className={aparencia(caminho.startsWith("/empresa")) + " sm:hidden"}
            >
              Empresa
            </Link>

            <Link
              href="/conta"
              aria-current={caminho.startsWith("/conta") ? "page" : undefined}
              className={aparencia(caminho.startsWith("/conta")) + " sm:hidden"}
            >
              Conta
            </Link>
          </nav>

          <div className="hidden flex-col gap-2 border-t border-marca-900 px-5 py-4 sm:flex">
            {/* A empresa fica no canto das configurações, e não no menu: mexe-se
                nela uma vez na instalação e quase nunca mais. */}
            <Link
              href="/empresa"
              className="text-xs font-semibold text-marca-200 underline-offset-2 hover:underline"
            >
              Empresa
            </Link>

            <Link
              href="/conta"
              className="truncate text-xs text-marca-300 underline-offset-2 hover:text-white hover:underline"
              title={usuario.email}
            >
              {usuario.email}
            </Link>
            <button
              type="button"
              onClick={encerrar}
              className="self-start text-xs font-semibold text-marca-200 underline-offset-2 hover:underline"
            >
              Sair
            </button>
          </div>
        </aside>

        <div className="flex min-w-0 flex-1 flex-col">{children}</div>
      </div>
    </ProvedorDeAvisos>
  );
}
