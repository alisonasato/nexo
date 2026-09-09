# Nexo

ERP. Codinome de trabalho — o nome de produto fica para quando houver cliente
(decisão Q32). Sucessor do Cuca, começando pelo back-office de escritório
contábil.

O que ficou decidido, e por quê, está em [DECISOES.md](DECISOES.md). O que
**não** entra agora está em [DEPOIS.md](DEPOIS.md), e essa separação é o que
segura o escopo.

## Estrutura

```
api/                     API em .NET 8
  Nexo.sln
  src/Nexo.Api/          Minimal API
    OpenApi/             O que molda o documento OpenAPI
    Endpoints/           Um arquivo por grupo de rotas
  openapi.json           O contrato. Versionado de propósito: mudança nele
                         aparece no diff antes de quebrar o front.
web/                     Front em Next.js 16
  src/api/cliente.ts     Cliente HTTP tipado
  src/api/esquema.d.ts   Gerado do openapi.json. Não versionado, não editar.
  src/app/               Rotas
```

## Requisitos

- .NET SDK 8
- Node 20 ou mais novo

## Rodar

Dois terminais, a partir da raiz:

```bash
npm run dev:api
```

```bash
npm run dev:web
```

A API sobe em `http://localhost:5240` (Swagger em `/swagger`), o front em
`http://localhost:3000`.

**Use o front pelo endereço dele.** O navegador nunca fala com a porta da API:
o front a serve sob `/api` no próprio domínio, por reescrita de rota. É o que
faz a exigência de mesmo domínio (decisão Q29) valer também em desenvolvimento,
em vez de aparecer como surpresa na primeira implantação. A página inicial chama `/saude` e mostra a resposta:
enquanto ela funcionar, o contrato entre os dois lados está fechado.

## O contrato

O cliente TypeScript **nunca** é escrito à mão. Ele é gerado a partir do
documento OpenAPI que a própria API descreve (decisão Q19). Depois de mexer em
qualquer endpoint ou DTO:

```bash
npm run contrato
```

Isso compila a API, reescreve `api/openapi.json` a partir do assembly — sem
precisar do servidor no ar — e regenera `web/src/api/esquema.d.ts`. Se o front
usava algo que sumiu, o `tsc` reclama na hora.

É um script em `ferramentas/gerar-contrato.mjs`, e não uma linha no
`package.json`, porque a extração sobe a aplicação inteira: ela precisa de uma
chave de assinatura efêmera e do ambiente `Testes`, senão a verificação de
arranque a derruba. Pior: o CLI do Swashbuckle engole essa exceção e reporta no
lugar dela que não achou uma classe `Startup`, que o modelo mínimo nem usa. O
comentário no script conta a história para o próximo que passar por ali.

Para conferir que os dois lados compilam:

```bash
npm run verificar
```

### Por que `PropriedadesNaoAnulaveisSaoObrigatorias`

O Swashbuckle marca o `nullable` mas não alimenta o `required`, e sem `required`
tudo sai opcional no documento — o front acabaria checando nulo em campo que
nunca é nulo. O filtro em `api/src/Nexo.Api/OpenApi/` lê a nulidade do próprio
CLR e corrige isso para todo DTO, presente e futuro.

## Onde este repositório mora

Fora do OneDrive, de propósito: a sincronização de `node_modules` e `obj/`
trava build de .NET e enche a fila com dezenas de milhares de arquivos.

## Banco local

O Postgres aqui é **portátil**: binários oficiais 17.7 descompactados em
`C:\dev\pgsql`, com os dados em `C:\dev\pgsql-dados`. Não há serviço do
Windows, não há entrada no registro, e some deletando as duas pastas. Foi a
escolha possível numa máquina sem Docker, sem WSL e sem administrador.

```bash
"C:/dev/pgsql/bin/pg_ctl" -D C:/dev/pgsql-dados -l C:/dev/pgsql-dados/postgres.log start
```

```bash
"C:/dev/pgsql/bin/pg_ctl" -D C:/dev/pgsql-dados stop
```

Dois bancos, os dois pertencentes ao papel `nexo`: `nexo` para desenvolver e
`nexo_testes` para os testes.

### Por que o papel `nexo` não é superusuário

Superusuário **ignora RLS por completo**, inclusive com `FORCE`. Se a
aplicação conectasse como superusuário, o teste de isolamento passaria a medir
coisa nenhuma. Por isso o cluster nasce com `postgres` como superusuário e
`nexo` como papel comum — `NOSUPERUSER`, `NOBYPASSRLS` —, dono das tabelas e
contido pelo `FORCE ROW LEVEL SECURITY` da migração.

## Testes

```bash
npm run testar
```

São testes de integração contra Postgres de verdade (decisão Q30) — banco em
memória não provaria nada sobre RLS. A conexão sai de `NEXO_TESTES_CONEXAO`,
ou do padrão `Host=localhost;Port=5432;Database=nexo_testes;Username=nexo;Password=nexo`.

O teste que importa é o de isolamento entre tenants. Ele foi verificado por
mutação, não só por passar: com `DISABLE ROW LEVEL SECURITY` os quatro casos
falham, com `NO FORCE` os quatro falham, e com a migração no estado correto os
quatro passam. Um teste de RLS que nunca falhou não vale nada, porque a RLS
falha em silêncio.

## Entrar

Ao subir com o banco vazio, o ambiente de desenvolvimento cria um tenant, uma
empresa e um usuário — só para haver como entrar enquanto não existe fluxo de
cadastro de cliente novo:

| | |
|---|---|
| e-mail | `contato@escritoriodemo.com.br` |
| senha | `NexoDev@2026` |

A semeadura tem dupla trava: só é chamada quando o ambiente é `Development` e,
mesmo lá, só age se não houver nenhum tenant.

```bash
curl -c cookies.txt -X POST http://localhost:5240/autenticacao/entrar -H "Content-Type: application/json" -d '{"email":"contato@escritoriodemo.com.br","senha":"NexoDev@2026"}'
```

```bash
curl -b cookies.txt http://localhost:5240/empresas
```

### Como a sessão funciona

O token é um JWT que viaja em **cookie `httpOnly`**, nunca no cabeçalho
`Authorization` e nunca em `localStorage` (decisão Q29): um XSS num ERP é
acesso ao dinheiro, e o que o JavaScript não alcança ele não rouba. O cookie é
`SameSite=Lax`, o que de quebra impede que outro site dispare POST autenticado.

Dentro do token vão `tenant_id` e `empresa_id`. O `tenant_id` é lido a cada
abertura de conexão e vira `app.tenant_id` na sessão do Postgres — é ele que a
política de RLS compara. A corrente é: cookie, token, claim, interceptor,
política.

**A autorização é fechada por padrão.** Rota nova nasce exigindo sessão; abrir
é um ato explícito com `AllowAnonymous`, como em `/saude`. O inverso disso é
como se esquece de proteger uma rota.

### A chave de assinatura

`Jwt:Chave` não tem valor padrão, e a aplicação **não sobe sem ela**. Um padrão
aqui seria um segredo versionado, e quem clonasse o repositório saberia assinar
tokens válidos. Em desenvolvimento ela está em `appsettings.Development.json`;
fora dele, use `dotnet user-secrets` ou variável de ambiente.

## Implantação

Configuração, migração automática e o que quebra fora da sua máquina estão em
[IMPLANTACAO.md](IMPLANTACAO.md). O passo a passo da Railway, com os serviços e
as variáveis de cada um, está em
[IMPLANTACAO-RAILWAY.md](IMPLANTACAO-RAILWAY.md).

## Migrações

```bash
dotnet ef migrations add NomeDaMigracao --project api/src/Nexo.Api --output-dir Dados/Migracoes
dotnet ef database update --project api/src/Nexo.Api
```

A política de RLS mora numa migração própria (`SegurancaPorLinha`), separada
do esquema, para que mudança nela apareça sozinha no diff. Tabela nova com
`tenant_id` nasce isolada na mesma migração, com
`migrationBuilder.AplicarIsolamentoPorTenant("tabela")` — assim esquecer a
política deixa de ser possível.

**Não há passo manual de migração.** A aplicação aplica o que estiver pendente
quando sobe, protegida por um trinco consultivo do Postgres para o caso de duas
instâncias subirem juntas. `dotnet ef database update` continua servindo para
adiantar o banco local.

## O que ainda não existe

**Cobrança automática pelo PSP.** É o único elo que falta no ciclo do dinheiro,
e ele depende de escolher um PSP e criar conta nele — está explicado em
[DECISOES.md](DECISOES.md).

O ciclo completo já funciona pela tela: cadastrar a pessoa, torná-la cliente,
criar o contrato, gerar as mensalidades da competência e dar baixa quando o
dinheiro entra (com estorno, se a baixa foi errada). O que mais falta, e por
que, está em [DEPOIS.md](DEPOIS.md).
