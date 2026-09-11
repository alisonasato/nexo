# Implantação

O que precisa estar configurado para o Nexo rodar fora da sua máquina.

Para a Railway especificamente, o passo a passo está em
[IMPLANTACAO-RAILWAY.md](IMPLANTACAO-RAILWAY.md).

Tudo aqui foi verificado rodando a API com `ASPNETCORE_ENVIRONMENT=Production`
localmente, contra um Postgres vazio. O que **não** deu para verificar está
marcado no fim.

## Configuração

Três variáveis, e nenhuma tem valor padrão útil:

| Variável | O quê |
|---|---|
| `DATABASE_URL` ou `ConnectionStrings__Nexo` | Aceita a URI `postgresql://…` ou os pares `Host=…;Port=…`. A explícita vence. |
| `Jwt__Chave` | Mínimo de 32 bytes. **Sem ela a aplicação não sobe** — de propósito. |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

O duplo sublinhado é a forma de escrever hierarquia de configuração em variável
de ambiente: `Jwt__Chave` vira `Jwt:Chave`.

Gere a chave com algo que não seja a sua imaginação:

```bash
node -e "console.log(require('crypto').randomBytes(48).toString('base64'))"
```

## A porta vem do ambiente

Quando existe a variável `PORT`, a aplicação liga em `[::]:$PORT` — que é
como todo PaaS diz onde escutar. Sem ela, nada muda: vale o `--urls` ou o
`launchSettings` de sempre.

O endereço é `[::]` e não `0.0.0.0` porque a rede privada entre serviços é
IPv6 na Railway; `[::]` atende os dois protocolos. Verificado aqui: com
`PORT=5299`, responde em `127.0.0.1` e em `[::1]`.

### As duas formas de string de conexão

O Npgsql quer `Host=…;Port=…`; quase todo PaaS entrega `postgresql://…`. A
aplicação aceita as duas e traduz, porque conversão à mão na hora do primeiro
deploy é onde se perde uma tarde: o serviço sobe, o `/saude` responde, e só a
primeira consulta quebra.

```bash
ASPNETCORE_ENVIRONMENT=Production DATABASE_URL="postgresql://usuario:senha@servidor:5432/banco" Jwt__Chave="…" dotnet run --project api/src/Nexo.Api --no-launch-profile
```

## O banco se migra sozinho

Na subida, a aplicação aplica as migrações pendentes. Não há passo manual de
implantação.

Se houver mais de uma instância, elas sobem juntas e disputariam a migração —
por isso existe um trinco consultivo do Postgres (`pg_advisory_lock`): a
segunda espera a primeira e depois não encontra nada a fazer. Ver
`Dados/MigracaoNaSubida.cs`.

Verificado: apontando a aplicação para um banco vazio, ela criou as 14 tabelas
e as 5 políticas de RLS sozinha.

### O papel do banco não pode ser dono

**Superusuário ignora RLS por completo, inclusive com `FORCE`.** O usuário da
string de conexão precisa ser um papel comum, sem `SUPERUSER` e sem `BYPASSRLS`.

**A aplicação confere isso sozinha na subida.** Em produção ela se recusa a
subir, dizendo o que criar; fora dela, apenas registra um aviso — quem
desenvolve às vezes aponta para um banco administrativo de propósito.

Isso existe porque era o único erro desta implantação **sem sintoma**: com
superusuário, tudo funciona e um escritório enxerga o dado do outro. Depender de
alguém lembrar de rodar uma consulta é o mesmo que não ter proteção.

Para conferir à mão, se quiser:

```bash
psql -c "SELECT rolname, rolsuper, rolbypassrls FROM pg_roles WHERE rolname = 'nexo';"
```

Os dois últimos precisam ser `f`.

## A única saída para fora

A aplicação chama **um** serviço de terceiro: o ViaCEP, para preencher endereço
a partir do CEP. A chamada sai do servidor, não do navegador.

| Variável | Padrão | Para quê |
|---|---|---|
| `Servicos__Cep__Endereco` | `https://viacep.com.br/` | Trocar por um espelho, ou apontar para o vazio |

Nada depende dela. O serviço fora do ar devolve 503 no `/enderecos/{cep}`, a
tela pede para digitar à mão, e o cadastro salva igual. Verificado apontando a
variável para um endereço inalcançável: a mensagem certa apareceu e o cadastro
foi salvo com o endereço digitado.

O tempo de espera é de quatro segundos, e é curto de propósito. Isto é um
atalho de digitação: se demorar mais do que digitar o endereço, deixou de ser
atalho.

Se a rede de saída do PaaS for restrita, libere `viacep.com.br` ou aponte a
variável para outro lugar. Esquecer disso não quebra nada, só desliga o atalho.

## Um domínio só, e isso é estrutural

A decisão Q29 exige front e API no mesmo domínio, porque o token viaja em
cookie `httpOnly` e cookie é por host. Isso **não** depende mais de você
configurar certo na hora de implantar.

O front serve a API no próprio domínio, por reescrita de rota
(`web/next.config.ts`): o navegador chama `/api/...` no host do front e o
servidor do Next repassa. Verificado — com a aplicação aberta, todas as
requisições do navegador vão para o host do front, nenhuma para a porta da API.

Consequências práticas, e são boas:

- **Só o front precisa de endereço público.** No PaaS, a API pode ficar numa
  rede interna, sem porta exposta.
- **Não há CORS a acertar.** Não existe requisição entre origens.
- **Desenvolvimento funciona como produção**, então erro de origem não aparece
  só depois de implantar.

O front descobre a API por `API_INTERNA` — **sem** o prefixo `NEXT_PUBLIC_`,
porque só o servidor a usa:

| Variável | Onde | O quê |
|---|---|---|
| `API_INTERNA` | serviço do front | Endereço interno da API, por exemplo `http://api:8080` |

## Atrás do proxy

O proxy do PaaS termina o TLS; até a aplicação a requisição chega em http. Ela
lê `X-Forwarded-Proto` e `X-Forwarded-For` para conhecer o esquema original, e
envia HSTS.

**Redirecionar http para https é trabalho do proxy**, e a aplicação não faz —
sem porta https configurada, o middleware do ASP.NET só registra um aviso e
deixa passar, que é proteção de mentira. Confirme que o seu PaaS redireciona.

Ligar `ForwardedHeaders` com as listas de proxies conhecidos vazias significa
confiar no salto imediatamente à frente. Vale enquanto só o proxy alcançar a
aplicação; se um dia ela ficar exposta direto, um cliente poderá afirmar "vim
por https" sem ter vindo.

## Rodar em modo produção na sua máquina

Serve para reproduzir problema que só aparece lá.

```bash
ASPNETCORE_ENVIRONMENT=Production ConnectionStrings__Nexo="…" Jwt__Chave="…" dotnet run --project api/src/Nexo.Api --no-launch-profile --urls http://localhost:5243
```

O `--no-launch-profile` **não é opcional**. Sem ele, o `dotnet run` lê o
`Properties/launchSettings.json`, que fixa `ASPNETCORE_ENVIRONMENT=Development`
e sobrescreve a variável de ambiente. A aplicação sobe achando que é
desenvolvimento e o teste não testa nada — foi exatamente o que aconteceu na
primeira tentativa daqui, e o `/saude` denunciou respondendo
`"ambiente":"Development"`.

## Duas coisas para saber antes de estranhar

- **Caminho desconhecido devolve 401, não 404.** Efeito da política padrão
  fechada, que também vale para requisições sem endpoint. Um usuário anônimo
  que erra a URL vê "não autorizado". É deliberado — não revela quais rotas
  existem —, mas confunde na hora de depurar.
- **HSTS não aparece em localhost.** O middleware exclui `localhost` e `127.0.0.1`
  por padrão. Testar com outro `Host` mostra o cabeçalho.

## O que já rodou em produção de verdade

Esta seção era uma lista de limites desta máquina. A implantação na Railway
transformou quase tudo em fato verificado:

- **Build e deploy da API pelo `Dockerfile`**, que passou a existir porque o
  Railpack não reconhece .NET. O front não tem `Dockerfile`: o Railpack detecta
  Node e constrói sozinho.
- **Proxy de verdade na frente**, com TLS e domínio público.
- **A reescrita de rota atravessa o proxy.** No domínio do front,
  `/api/saude` responde `"ambiente":"Production"` e
  `/api/autenticacao/entrar` com credencial errada devolve 401 com o JSON da
  aplicação. Tudo na mesma origem, sem redirecionamento no caminho.
- **O papel do banco sem `BYPASSRLS`**, com a conferência da subida passando.

O que continua sem verificação:

- **O backup automático da Railway.** O procedimento de backup e restauração
  está exercitado e escrito em [BACKUP.md](BACKUP.md), com duas armadilhas que
  só apareceram fazendo — inclusive uma que devolve o banco sem isolamento entre
  escritórios, e sem sintoma. O que falta é conferir no painel se o backup
  automático está ligado, e restaurar a partir de um arquivo dele.
