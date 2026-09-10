/*
 * Gera o contrato: compila a API, extrai o documento OpenAPI do assembly e
 * regenera o cliente TypeScript do front.
 *
 * Existe como script, e não como uma linha no package.json, por causa de um
 * detalhe chato: a extração sobe a aplicação inteira, e desde o passo 3 ela se
 * recusa a subir sem chave de assinatura — de propósito, para que produção sem
 * chave caia em vez de assinar tokens com um segredo versionado. O CLI do
 * Swashbuckle engole essa exceção e reporta no lugar dela uma mensagem
 * enganosa, dizendo que não achou uma classe `Startup` (que o modelo mínimo
 * nem usa). Perde-se um bom tempo procurando no lugar errado.
 *
 * A chave abaixo serve só para esta extração: o processo vive alguns segundos,
 * não escuta porta nenhuma e não assina token de ninguém. O ambiente é `Testes`
 * para que a semeadura de desenvolvimento não rode — assim gerar o contrato
 * não exige banco no ar.
 */

import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

const raiz = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const assembly = "api/src/Nexo.Api/bin/Debug/net8.0/Nexo.Api.dll";

const ambienteDaExtracao = {
  ...process.env,
  ASPNETCORE_ENVIRONMENT: "Testes",
  Jwt__Chave: "chave-efemera-so-para-extrair-o-documento-openapi",

  /*
   * Uma string de conexão que não conecta com nada.
   *
   * A extração monta a aplicação inteira para ler as rotas, e montar inclui
   * resolver a configuração do banco — que hoje se recusa a ficar vazia, para
   * que produção sem banco caia na subida em vez de na primeira consulta.
   * Nenhuma conexão é aberta aqui: o processo lê as rotas e morre.
   *
   * Sem isto, gerar o contrato falha, e a falha é silenciosa para quem
   * redireciona a saída — foi assim que ela passou despercebida uma vez.
   */
  ConnectionStrings__Nexo: "Host=nao-conecta;Database=nao-conecta;Username=x;Password=x",

  /*
   * Pula migração e provisionamento. A extração executa a aplicação até a
   * tabela de rotas ficar montada, e sem isto ela tentaria falar com o banco —
   * gerar o contrato passaria a exigir Postgres no ar, em toda máquina e no CI.
   */
  NEXO_EXTRAINDO_CONTRATO: "1",
};

const passos = [
  {
    nome: "compilando a API",
    comando: "dotnet",
    argumentos: ["build", "api/Nexo.sln", "-v", "q", "--nologo"],
    ambiente: process.env,
  },
  {
    nome: "extraindo o OpenAPI",
    comando: "dotnet",
    argumentos: ["swagger", "tofile", "--output", "api/openapi.json", assembly, "v1"],
    ambiente: ambienteDaExtracao,
  },
  {
    /*
     * Chamado direto pelo Node, e não por `npm run`, porque no Windows o npm é
     * um .cmd — e desde a correção de segurança de 2024 o Node recusa executar
     * .cmd sem shell, enquanto usar shell reintroduz o problema de escape que
     * acabamos de tirar. Apontar para o cli.js do pacote evita os dois.
     */
    nome: "gerando o cliente TypeScript",
    comando: process.execPath,
    argumentos: [
      "web/node_modules/openapi-typescript/bin/cli.js",
      "api/openapi.json",
      "--output",
      "web/src/api/esquema.d.ts",
    ],
    ambiente: process.env,
  },
];

for (const passo of passos) {
  process.stdout.write(`> ${passo.nome}\n`);

  /* Sem `shell: true`: o Node avisa, com razão, que argumentos por shell não são escapados. */
  const resultado = spawnSync(passo.comando, passo.argumentos, {
    cwd: raiz,
    env: passo.ambiente,
    stdio: "inherit",
  });

  if (resultado.status !== 0) {
    process.stderr.write(`\nFalhou em "${passo.nome}".\n`);
    process.exit(resultado.status ?? 1);
  }
}

process.stdout.write("\nContrato atualizado: api/openapi.json e web/src/api/esquema.d.ts\n");
