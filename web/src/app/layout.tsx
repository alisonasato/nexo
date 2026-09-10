import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";

import { Provedores } from "./provedores";
import "./globals.css";

const geistSans = Geist({ variable: "--font-geist-sans", subsets: ["latin"] });
const geistMono = Geist_Mono({ variable: "--font-geist-mono", subsets: ["latin"] });

export const metadata: Metadata = {
  title: "Nexo",
  description: "ERP — back-office de escritório contábil.",
};

/*
 * O tipo das props é escrito à mão, e não `LayoutProps<"/">`.
 *
 * `LayoutProps` é um global que o Next **gera** durante o build, dentro de
 * `.next/types`. Num clone limpo esse diretório não existe, e o `tsc` falha
 * com "Cannot find name 'LayoutProps'" antes de qualquer build acontecer — o
 * que quebra a verificação de tipos no CI e para quem acabou de clonar.
 *
 * É a mesma razão pela qual as páginas com parâmetro declaram
 * `{ params: Promise<...> }` à mão. Este arquivo veio do create-next-app e
 * ficou para trás quando as outras foram corrigidas.
 */
export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html
      lang="pt-BR"
      className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}
    >
      <body className="min-h-full flex flex-col">
        <Provedores>{children}</Provedores>
      </body>
    </html>
  );
}
