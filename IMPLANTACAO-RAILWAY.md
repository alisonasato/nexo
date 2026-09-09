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

Adicione o plugin de Postgres ao projeto. Ele publica variáveis próprias; a que
interessa é a URL de conexão.

**A string do Postgres da Railway vem no formato URI** (`postgresql://…`), e o
Npgsql espera pares `Host=…;Port=…`. Converta ao preencher a variável da API,
ou o serviço sobe e falha na primeira consulta.

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

## Serviço `api`

| Ajuste | Valor |
|---|---|
| Root directory | `api/src/Nexo.Api` |
| Domínio público | **nenhum** — só rede privada |

Variáveis:

| Variável | Valor |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__Nexo` | `Host=…;Port=5432;Database=…;Username=…;Password=…` |
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

| Ajuste | Valor |
|---|---|
| Root directory | `web` |
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
   registra isso. **Depois de entrar, remova as cinco variáveis**: elas guardam
   uma senha em texto no painel, e não servem mais para nada (a criação só
   acontece com banco vazio).

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
