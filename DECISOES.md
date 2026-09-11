# Decisões

O registro completo, com o porquê de cada uma, está publicado em
<https://claude.ai/code/artifact/5aa65661-0a52-43e4-8c47-a08d61beae86>.

Este arquivo é o resumo operacional: o que muda o código, e o que impede
mudanças no código. Cada decisão tem um número (`Q##`) que aparece citado nos
comentários do próprio código — quando um comentário disser "decisão Q29", é
aqui que se procura.

## O que o produto é

| | |
|---|---|
| **Q12** | O Nexo é o sucessor do Cuca. O Cuca está congelado e serve como **especificação de leitura** — 132 DTOs de domínio validado em produção. |
| **Q13** | Fatiar por vertical: entregar só o que o segmento escolhido usa. |
| **Q15** | A primeira vertical é o escritório de contabilidade. |
| **Q18** | Dentro dela, **só o back-office do próprio escritório**: clientes, contratos, faturamento recorrente, recebíveis. Nada de escrituração, obrigações ou folha. |
| **Q3** | Produto multi-tenant, ao contrário do Cuca, que é uma instalação por cliente. |

## Arquitetura

| | |
|---|---|
| **Q16** | API em .NET sobre **Postgres**. Relacional, não documental. |
| **Q19** | Monorepo, e o **contrato entre front e API é gerado**, nunca escrito à mão. |
| **Q22** | Isolamento por **linha, com RLS do Postgres**. Dois níveis: `tenant` (o cliente do ERP) e `empresa` (o estabelecimento, matriz e filial). O Cuca fundiu os dois e por isso não virou produto. |
| **Q28** | EF Core, com interceptor aplicando `SET LOCAL` a cada transação. |
| **Q26** | ASP.NET Core Identity, com `tenant_id` e `empresa_id` em claims. A identidade fica em **uma tabela só**; o id do usuário não vira chave estrangeira espalhada pelo domínio. |
| **Q29** | Busca de dados **no cliente**, com TanStack Query e cookie `httpOnly`. Nunca token em `localStorage`. |
| **Q20** | Do protótipo ContaGestor vêm **só o design system e os primitivos**. O CRUD declarativo é reescrito, já assíncrono. |
| **Q31** | PaaS enquanto o custo não doer. |

### As três armadilhas da RLS

Erre qualquer uma e a política vira decoração: tudo funciona, inclusive ver o
dado do vizinho.

1. O usuário da aplicação **não pode ser dono das tabelas** — dono ignora RLS,
   a menos que a tabela tenha `FORCE ROW LEVEL SECURITY`.
2. Não pode ter `BYPASSRLS` nem ser superusuário.
3. O tenant precisa ser regravado em **toda abertura de conexão**, sem
   condição. O risco é o pool entregar uma conexão ainda marcada com o tenant
   da requisição anterior, e gravar sempre fecha esse buraco — é o que o
   `InterceptorDeTenant` faz. (`SET LOCAL` dentro da transação resolveria o
   mesmo problema, mas obrigaria toda leitura a abrir transação.) Nada disso
   vale com multiplexação do Npgsql ligada, porque aí a sessão deixa de
   pertencer a uma conexão lógica.

### As tabelas que não têm RLS, e por quê

Três tabelas ficam de fora da política: `tenants` e as duas do Identity
(`asp_net_users` e suas companheiras). Não é esquecimento, é consequência.

O login precisa achar o usuário pelo e-mail **antes** de existir qualquer
tenant conhecido — é a busca que descobre qual é o tenant. Uma política de
linha não tem o que comparar nesse momento, e qualquer regra do tipo *quando
não há tenant, libera* não protegeria nada: só pareceria proteger, que é pior
do que não ter.

A contrapartida é uma regra, não uma observação:

> **Toda consulta a usuários fora do caminho do login filtra por tenant
> explicitamente.** Nessas três tabelas o `WHERE` esquecido volta a ser
> perigoso, porque o banco não segura.

Em compensação, o resto do sistema não depende de disciplina: a claim do token
alimenta `app.tenant_id`, e o endpoint `/empresas` — que é o primeiro a ler
dado de negócio — **não tem filtro de tenant nenhum na consulta**. Quem filtra
é o Postgres.

## A primeira entrega

| | |
|---|---|
| **Q24** | Pessoas, contratos, mensalidades, recebíveis e **cobrança automática com baixa automática**. |
| **Q25** | O dinheiro entra por um **PSP** — boleto e Pix em uma integração, baixa por webhook. Nada de CNAB. |
| **Q21** | **NFS-e fica para a segunda entrega**, via agregador. Até lá, a nota sai no portal da prefeitura, como já sai hoje. |
| **Q30** | Só teste de **integração da API**. Um deles é obrigatório desde o primeiro dia: o que prova que um tenant não lê o outro. O segundo, quando a cobrança entrar, é o webhook do PSP. |

Fora do escopo: contas a pagar, caixa, estoque, NF-e, escrituração, migração
de dados do Cuca.

### Total nenhum vem da página

Toda soma que aparece na tela é feita **no banco, sobre o conjunto inteiro**.
Somar a lista carregada é o jeito mais fácil de produzir um número errado com
cara de certo: com paginação, "em aberto" passaria a mostrar só o que coube
na tela, e ninguém desconfiaria — o valor é plausível, só está errado.

Vale para o total em aberto, o vencido, o recebido e o faturamento mensal
recorrente dos contratos.

E os totais de recebíveis seguem a **competência**, não o filtro de situação.
Filtrar por "Pagos" e ver "em aberto: R$ 0,00" seria honesto e inútil: a
pergunta que o escritório faz enquanto olha o que já entrou é quanto ainda
falta entrar. A lista mostra a fatia escolhida; os totais mostram o mês.

### Por que o índice de mensalidade ignora cancelados

O par `contrato + competência` é único **entre os não cancelados**. O recorte
não é detalhe: um índice cego à situação transforma um erro comum em erro
permanente.

O caso: o valor do contrato estava errado, as mensalidades foram geradas, o
contrato foi corrigido. Com índice cego, a competência fica ocupada para
sempre por um recebível errado — não dá para apagar, porque histórico de
cobrança não se apaga, e não dá para gerar de novo, porque o índice recusa.

Cancelar sai da conta e libera a competência, sem sumir do histórico. Por
isso cancelar exige motivo por escrito, e por isso não se cancela o que já
foi baixado — isso apagaria uma entrada de dinheiro sem devolver nada a
ninguém.

## A disciplina

| | |
|---|---|
| **Q17** | Uma pessoa, tempo parcial. É a restrição que dimensiona todas as outras. |
| **Q23** | Não há primeiro usuário ainda. Risco assumido, não resolvido. |
| **Q27** | **Lista congelada**: o escopo acima não cresce até estar em produção. Ideia nova vira linha em [DEPOIS.md](DEPOIS.md), nunca um commit. |

## O elo que falta: cobrança automática

A decisão Q25 escolheu **PSP** — boleto e Pix numa integração só, baixa por
webhook. Isso continua valendo. O que impede de construir agora não é o
código: é que a integração exige **uma conta num PSP específico**, e cada um
tem uma API diferente.

Escrever a interface antes de escolher o PSP seria adivinhar a forma dela —
o mesmo erro que a Q20 evitou com o motor de telas. Uma abstração desenhada
sobre nenhuma implementação real acerta por acidente ou atrapalha para sempre.

**O que falta decidir, e é seu:** qual PSP (Asaas, Cobre Fácil, Iugu,
Pagar.me), e criar a conta. Com isso em mãos, o que entra é:

1. A emissão da cobrança a partir de um recebível — boleto e Pix.
2. O webhook de pagamento, com verificação de assinatura, dando baixa com
   `OrigemDaBaixa = "cobranca"` (a coluna já existe e já é gravada).
3. Idempotência do webhook: o PSP reenvia notificação, e reenvio não pode
   virar baixa dupla. É o mesmo problema que o índice único resolveu na
   geração de mensalidade, e merece a mesma solução no banco.

A baixa **manual** já funciona, com estorno. O escritório consegue operar
sem o PSP — conferindo o extrato à mão, que é o que ele faz hoje. O PSP é o
que elimina essa conferência, e era a razão da Q24 escolher a opção 2.

## Sobre o motor de telas declarativas

A decisão Q20 descartou o CRUD declarativo do protótipo como código e disse
que a **ideia** renasce assíncrona. Ela ainda não renasceu, de propósito:
generalizar na primeira tela é adivinhar a forma. A primeira tela foi escrita
concreta, com primitivos pequenos em `web/src/componentes/controles.tsx`.

O motor sai por **extração**, quando a segunda ou terceira tela mostrar o que
de fato se repete — e não antes.

## Sobre centralizar o cadastro de pessoas

Analisado em 11 de setembro de 2026, e **deixado como está**, de propósito.

O cadastro já é central onde precisa ser. `Pessoa` é o registro, e `Cliente` é
um **vínculo**: aponta para uma pessoa e acrescenta o que só vale para o papel
de cliente. O mesmo CNPJ pode ser cliente e fornecedor sem virar dois cadastros,
porque o que muda de papel para papel é o vínculo. Quando fornecedor existir,
ele nasce como mais uma tabela de vínculo, e o cadastro não muda.

Sobrou uma incoerência, e ela é real: **`Empresa` é um cadastro paralelo.**
Guarda razão social, nome fantasia e CNPJ — que são nome, nome fantasia e
documento de `Pessoa` — e não se liga a ela. E tem menos: não tem endereço nem
contato, que a NFS-e vai exigir do estabelecimento.

**Não foi corrigida agora**, e o motivo não é preguiça. `Empresa` não é um
terceiro: é o escritório, e a corrente tenant → empresa é a espinha do
isolamento que a Q22 separou justamente para o Nexo não virar instalação por
cliente. Pendurar a espinha numa tabela de domínio acopla as duas. Some-se que
`Empresa.Cnpj` é obrigatório e `Pessoa.Documento` pode ficar vazio: virando
vínculo, essa garantia precisa de outro lugar para morar.

A conta que decide é o prazo. Hoje é **um registro de empresa** por escritório,
e a migração é barata; ela encarece com cada escritório novo. Mas o que a
mudança compra só é cobrado na NFS-e, que está no `DEPOIS.md`. Então a hora de
fazer é quando a NFS-e entrar, junto — e não antes, por simetria.

A análise achou uma falta mais urgente que a incoerência, e essa virou linha no
`DEPOIS.md`: **ninguém consegue editar a empresa do próprio escritório.** O
endpoint só tem `GET`, nenhuma tela usa, e o provisionamento escreve os dados
uma vez. CNPJ digitado errado na variável de ambiente não tem conserto pela
aplicação.

## Ordem de execução

1. ~~Repositório e contrato~~ — feito.
2. ~~Banco, RLS e o teste de vazamento~~ — feito.
3. ~~Autenticação~~ — feito.
4. ~~A primeira tela~~ — feito. Entrada e cadastro de pessoas.
5. **O ciclo do dinheiro** — quatro dos cinco elos feitos.
   - ~~Cliente, contrato, mensalidade, recebível e baixa manual~~ — feito.
   - **Cobrança automática pelo PSP** — parada, e não por falta de tempo:
     depende de escolher o PSP e ter conta nele. Ver abaixo.
6. **Cobrança e baixa automática.** Depende de uma conta em PSP.

**No ar desde 10 de setembro de 2026**, na Railway: três serviços — front com
domínio público, API só na rede privada, e Postgres. O papel do banco não é
superusuário e não tem `BYPASSRLS`, então a RLS vale também em produção. O
passo a passo e as armadilhas estão em
[IMPLANTACAO-RAILWAY.md](IMPLANTACAO-RAILWAY.md).
