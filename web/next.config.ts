import type { NextConfig } from "next";

/**
 * O front serve a API no próprio domínio, por reescrita de rota.
 *
 * A decisão Q29 exige front e API no mesmo domínio, porque o token viaja em
 * cookie `httpOnly` — e cookie é por host. Enquanto isso era só uma instrução
 * no documento de implantação, era também uma armadilha: separar os domínios
 * quebraria o login **sem erro nenhum**, o usuário simplesmente nunca entraria.
 *
 * Com a reescrita, o navegador só conhece um endereço. Ele chama `/api/...` no
 * mesmo host do front, e o servidor do Next repassa para a API. Não há
 * requisição entre origens, então não há CORS a acertar, nem `SameSite` a
 * negociar — e desenvolvimento passa a funcionar exatamente como produção.
 *
 * `API_INTERNA` **não** leva o prefixo `NEXT_PUBLIC_`, de propósito: o endereço
 * da API é usado só no servidor. O navegador nunca precisa saber onde ela está,
 * e no PaaS ela pode ficar numa rede interna, sem porta pública.
 */
const nextConfig: NextConfig = {
  async rewrites() {
    const api = process.env.API_INTERNA ?? "http://localhost:5240";

    return [{ source: "/api/:caminho*", destination: `${api}/:caminho*` }];
  },
};

export default nextConfig;
