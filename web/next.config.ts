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
  /*
   * Recebíveis virou Lançamentos. O endereço antigo continua levando à tela,
   * com o recorte da URL junto: ele pode estar em favorito ou num link mandado
   * a alguém. Temporário, e não permanente, para o navegador não guardar para
   * sempre um desvio que um dia pode mudar.
   */
  async redirects() {
    return [{ source: "/recebiveis", destination: "/lancamentos", permanent: false }];
  },

  async rewrites() {
    const api = process.env.API_INTERNA ?? "http://localhost:5240";

    return [{ source: "/api/:caminho*", destination: `${api}/:caminho*` }];
  },
};

export default nextConfig;
