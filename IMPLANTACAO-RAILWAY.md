# Implantar na Railway

O passo a passo específico. As regras gerais — variáveis, migração automática,
mesmo domínio — estão em [IMPLANTACAO.md](IMPLANTACAO.md).

São **três serviços** num projeto: Postgres, `api` e `web`. Só o `web` fica
público.

```
internet ──► web (Next.js, domínio público)
                │  reescrita /api/* → api.railway.internal
                ▼
              api (.NET, rede privada)
                │
                ▼
             Postgres
```

## Antes de começar

Duas coisas que só você pode fazer, e a primeira é bloqueio:

1. **O repositório não tem nenhum commit ainda.** Sem isso não há o que
   implantar por nenhum dos dois caminhos.
2. **Escolher como o código chega na Railway** — ver a seção seguinte.

## Como o código chega lá

**Por GitHub, que é o que eu recomendo.** A Railway acompanha o repositório e
reimplanta a cada push, e é o mesmo repositório que a integração contínua vai
usar depois. Exige criar o repositório no GitHub e apontar o remoto.

**Pela CLI**, com `railway up`, que envia o diretório local. Não precisa de
GitHub, mas cada implantação passa a ser um comando manual seu, e nada registra
o que foi implantado. Serve para experimentar.

A CLI não está instalada nesta máquina, e o `gh` também não.

## Postgres

Adicione o plugin de Postgres ao projeto e **ligue-o ao serviço `api`**. Ao ligar,
a Railway injeta `DATABASE_URL` no serviço, e é só disso que a aplicação precisa
— não há string de conexão para copiar.

A URL vem no formato URI (`postgresql://usuário:senha@servidor:5432/banco`) e o
Npgsql espera pares `Host=…;Port=…`. A aplicação faz a tradução sozinha,
inclusive decodificando senha com `@`, `/` e `+`, que é o que costuma vir de
senha gerada por PaaS. Verificado: a aplicação sobe e consulta o banco tendo
apenas `DATABASE_URL` no ambiente.

Se quiser apontar para outro banco, `ConnectionStrings__Nexo` vence o
`DATABASE_URL` — e aceita as duas formas também.

### O papel do banco

O `IMPLANTACAO.md` avisa que **superusuário ignora RLS por completo, inclusive
com `FORCE`** — e o usuário que a Railway cria costuma ser o dono do banco. Esse
é o ponto mais perigoso de toda esta implantação: com um papel superusuário, o
isolamento entre escritórios simplesmente não existe, e nada acusa.

Depois de subir, confira. Os dois últimos precisam ser `f`:

```bash
psql "$DATABASE_URL" -c "SELECT rolname, rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user;"
```

Se não forem, crie um papel comum, dê a ele posse das tabelas e use-o na string
de conexão da aplicação.

## Criar um serviço a partir do repositório

Os dois serviços saem do **mesmo repositório**. Isso parece errado na hora de
fazer, e é o desenho: cada um olha uma pasta diferente do mesmo código.

O **root directory não é campo do formulário de criação**. Ele aparece só
depois, nas Settings do serviço já criado — procurar por ele antes é procurar
o que não existe.

1. No canvas do projeto, **New**
2. **Connect Repo**, e escolha o repositório
3. **O primeiro build vai falhar**, e é esperado: sem root directory, a Railway
   olha a raiz do repositório e não sabe o que construir
4. Abra o serviço → **Settings** → **Root Directory**
5. **Variables** → as da tabela correspondente
6. Reimplante

### Como cada serviço é construído

A `api` tem um **Dockerfile**, em `api/src/Nexo.Api/`. Não é preferência: o
Railpack, construtor automático da Railway, **não suporta .NET** — a própria
documentação deles manda usar Dockerfile para ASP.NET Core.

O `web` **não** tem Dockerfile, e não precisa: Railpack detecta Node e Next
sozinho.

### O erro que aparece quando o root directory não foi definido

```
↳ Detected Node
✖ No start command detected
```

É o sintoma clássico. Sem root directory, o construtor olha a **raiz do
repositório**, encontra o `package.json` dos scripts de orquestração — que não
tem dependências nem `start` — e conclui que o projeto é Node.

Se você viu isso, o serviço está construindo a pasta errada. Vá em Settings e
defina o Root Directory.

### Se o repositório não aparece na lista

Na ordem, do mais barato para o mais drástico:

1. **Add → GitHub Repository → Refresh**, para forçar a atualização do cache.
   É o caso mais comum quando o repositório foi criado depois da conexão.
2. Em [github.com/settings/installations](https://github.com/settings/installations),
   abra o **Railway**. Se estiver em *Only select repositories*, acrescente o
   repositório. **Ser público não basta** — a Railway lista o que o app dela
   enxerga, e o app só enxerga o que foi autorizado.
3. Confira se a conta Railway está mesmo ligada ao GitHub. Conta criada com
   e-mail e senha não tem ligação nenhuma, e nenhuma lista vai aparecer.
4. Último recurso: desinstalar e reinstalar o app da Railway no GitHub.

## Serviço `api`

| Ajuste | Valor |
|---|---|
| Root directory (em Settings) | `api/src/Nexo.Api` |
| Domínio público | **nenhum** — só rede privada. Se a Railway criar um, remova. |

Variáveis:

| Variável | Valor |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `DATABASE_URL` | Injetada pela Railway ao ligar o Postgres ao serviço. Não precisa digitar. |
| `Jwt__Chave` | 32 bytes ou mais, gerada, nunca inventada |
| `PORT` | `8080` — **definida à mão, de propósito.** Ver abaixo. |
| `Provisionamento__TenantNome` | Nome do escritório. Só no primeiro deploy. |
| `Provisionamento__Email` | Quem vai entrar primeiro. Só no primeiro deploy. |
| `Provisionamento__Senha` | Forte. Só no primeiro deploy. |
| `Provisionamento__EmpresaRazaoSocial` | Razão social do escritório. |
| `Provisionamento__EmpresaCnpj` | Com ou sem pontuação. |

Gere a chave:

```bash
node -e "console.log(require('crypto').randomBytes(48).toString('base64'))"
```

### Por que fixar o `PORT` da API

A Railway injeta um `PORT` em cada serviço, e a aplicação já o respeita: ela
liga em `[::]:$PORT`. Verificado aqui — com `PORT=5299` ela responde tanto em
IPv4 quanto em IPv6, que é o que a rede privada deles exige.

O problema é o outro lado. O serviço `web` precisa escrever o endereço da API,
e a documentação da Railway é explícita: **o `PORT` usado em variável de
referência não resolve para a porta injetada em tempo de execução** — só
funciona se você tiver definido `PORT` à mão no serviço. Por isso o valor fixo:
sem ele, o `web` aponta para uma porta errada e recebe "application failed to
respond".

## Serviço `web`

Criado do mesmo jeito, **a partir do mesmo repositório**.

| Ajuste | Valor |
|---|---|
| Root directory (em Settings) | `web` |
| Domínio público | **sim** — é o único endereço que existe para o usuário |

Variáveis:

| Variável | Valor |
|---|---|
| `API_INTERNA` | `http://api.railway.internal:8080` |

Se você nomear o serviço da API com outro nome, troque o `api` do endereço.

Não existe `NEXT_PUBLIC_API_URL`, e a ausência é o desenho: o navegador nunca
conhece o endereço da API. Ele chama `/api/...` no domínio do `web`, e o
servidor do Next repassa pela rede privada.

## Depois do primeiro deploy

Na ordem, porque cada uma depende da anterior:

1. **`/api/saude` pelo domínio público** deve responder `"ambiente":"Production"`.
   Se responder `Development`, a variável de ambiente não chegou.
2. **Entre com a credencial de `Provisionamento`.** Com o banco vazio, a
   aplicação cria o tenant, a empresa e o usuário na primeira subida — e o log
   registra isso. **Depois de entrar, troque a senha em Conta → Trocar a senha
   e remova as cinco variáveis**: elas guardam uma senha em texto no painel, e
   não servem mais para nada (a criação só acontece com banco vazio). Remover
   sem trocar deixa a senha provisionada valendo — só a tira de vista.

   Sem essas variáveis, produção **não cria nada** — nem tenant de
   demonstração. É deliberado: senha conhecida nascendo sozinha num sistema com
   dinheiro dentro seria pior do que não ter provisionamento.
3. **Confira o papel do banco**, como acima. É o passo que ninguém lembra e o
   único cujo erro é invisível.
4. **Confirme que a Railway redireciona http para https.** A aplicação não faz
   isso — é trabalho do proxy, e o motivo está no `IMPLANTACAO.md`.
5. **Backup.** Veja se está ligado e restaure uma vez para um banco de teste.
   Backup não testado é backup que não existe.

## O que ainda não foi verificado

Nada nesta página passou por uma implantação real — não há conta em PaaS nem
CLI nesta máquina. O que **foi** verificado localmente, com um ensaio do deploy inteiro contra um
banco vazio em modo `Production`:

- A aplicação liga na porta de `PORT`, em IPv4 e IPv6.
- Sobe e consulta o banco tendo só `DATABASE_URL` em forma de URI.
- A migração acontece sozinha na subida: 14 tabelas e as 5 políticas de RLS.
- **Sem** as variáveis de `Provisionamento`, nada é criado: zero tenants, zero
  usuários.
- **Com** elas, o tenant, a empresa e o usuário nascem, e o login funciona.
- Subir de novo não duplica nada: continua um tenant, um usuário, e a migração
  responde que o banco já está na versão da aplicação.
- A reescrita `/api/*` funciona com o front em modo produção.
- Em `Production`, a aplicação sobe sem avisos, envia HSTS e conhece o esquema
  original atrás de um proxy.

O primeiro deploy é que vai dizer se os nomes de serviço, o *root directory* e a
detecção de build estão certos. Traga o log e eu ajusto.
