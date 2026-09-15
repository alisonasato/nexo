# O módulo financeiro, em fases

Contas a pagar e caixa estavam no `DEPOIS.md` como deixados de fora da primeira
entrega. Em 13 de setembro de 2026 a regra daquele arquivo foi **suspensa para
este módulo**, por decisão de quem é dono do projeto, escolhendo implementar em
fases. Este arquivo é o plano: o que cada fase entrega, o modelo de dados a que
o módulo chega, e como a tela se divide em componentes.

A regra do `DEPOIS.md` continua valendo para todo o resto.

## Onde se parte

O que já existe é o lado **a receber**, e ele é o esqueleto do módulo:

- Recebível com competência, vencimento, valor e situação.
- Baixa manual com valor e data, estorno, cancelamento com motivo obrigatório.
- Recorrência vinda dos contratos, com o índice único que impede gerar a mesma
  mensalidade duas vezes.
- Cobrança pelo Asaas, com webhook idempotente dando baixa.
- Totais somados no banco, nunca na página.

O que não existe: a pagar, contas bancárias, saldo, plano de contas, centro de
custo, parcelamento, renegociação, baixa em lote e trilha de auditoria geral.

## As fases

### Fase 1 — Lançamentos a receber

A tela de Recebíveis vira a tela de Lançamentos, com as operações avançadas
sobre o que já existe. Sai em três PRs:

1. **Cobrança segura.** Um defeito precisava ser fechado antes de qualquer
   operação nova. Cancelar ou baixar à mão um recebível cobrado não tirava a
   cobrança do Asaas: o boleto e o Pix continuavam pagáveis, o cliente pagava,
   e o aviso chegava para um recebível que não estava mais em aberto. A baixa
   era descartada sem alerta, e o dinheiro entrava sem registro. Agora cancelar
   e baixar à mão retiram a cobrança do PSP antes, o pagamento que chega para
   quem não esperava fica marcado como divergência, e a situação do recebível
   virou trava de concorrência. A renegociação cancela títulos, então dependia
   disto.
2. **Operações.** Parcelamento ao lançar, renegociação, baixa em lote, busca
   e período de vencimento na lista, e a consulta dos pagamentos marcados como
   divergência. Os totais ficam como estão: seguem a competência, e não a busca.
3. **A tela.** Recebíveis vira Lançamentos, e o endereço antigo redireciona.
   Cards de resumo, busca e período na URL, seleção do que está em aberto com
   barra de baixa em lote, gavetas para lançar (inteiro ou parcelado, com a
   prévia das parcelas) e renegociar, e cores de situação, com vence hoje em
   âmbar. E um aviso dos pagamentos marcados como divergência: o primeiro PR os
   grava e registra em log, mas nenhuma tela os mostrava antes deste.

### Fase 2 — Contas a pagar

A segunda natureza de lançamento, com fornecedor no lugar de cliente. A escolha
entre tabela única e tabelas irmãs foi feita aqui, como previsto: tabela única,
com natureza. O porquê está em "Uma tabela de lançamentos, com natureza", no
`DECISOES.md`. Sai em três PRs:

1. **A renomeação.** `recebiveis` vira `lancamentos` na tabela, na API e no
   contrato, sem mudar comportamento, por uma migração que renomeia em vez de
   recriar.
2. **A natureza a pagar na API.** A coluna de natureza, fornecedor no lugar de
   cliente, as operações valendo para as duas naturezas, e contrato,
   mensalidade e cobrança recusando lançamento a pagar.
3. **A tela.** Abas A receber e A pagar, com os textos e as ações de cada uma.

### Fase 3 — Contas bancárias e caixa

Cadastro de contas, saldo e movimentos. A baixa passa a exigir a conta de
destino ou de origem, e nasce o fluxo de caixa projetado contra o realizado.
Saldo é **derivado** de saldo inicial mais movimentos, nunca um número editável:
é o mesmo motivo pelo qual nenhum total vem da página. Sai em quatro PRs:

1. **O cadastro de contas.** Nome, tipo, banco, agência e número, e o saldo
   inicial com o dia em que valia. O saldo inicial é o do **começo** do dia: o
   que entra ou sai naquela data conta depois dele. Conta não se apaga, fica
   inativa.
2. **A baixa com conta.** Toda baixa nova diz em que conta o dinheiro entrou ou
   saiu e vira um movimento nela; o saldo de hoje passa a existir. O estorno à
   mão apaga o movimento, porque corrige um registro que não devia existir; o
   estorno do PSP compensa, com a devolução ao lado da entrada, porque o
   dinheiro entrou de fato. Baixa com data anterior ao saldo inicial da conta
   é recusada, porque aquele dinheiro já está dentro do saldo inicial. As
   baixas de antes desta fase ficam sem movimento, e o saldo inicial já as
   contém. A baixa que chega pelo PSP vai para a conta marcada para receber as
   cobranças, e cobrar exige que ela exista; se a marca sumir depois, o aviso
   vira divergência em vez de baixa. O saldo inicial não muda depois do
   primeiro movimento.
3. **O fluxo de caixa.** O realizado, dos movimentos, contra o projetado, dos
   lançamentos em aberto que vencem de hoje em diante, por dia ou por mês e
   com o saldo acumulado. O que está em atraso aparece à parte e fora do
   saldo projetado, e o saldo inicial de uma conta entra no dia em que ela
   começa.
4. **Transferências e movimentos avulsos.** Dinheiro que muda de conta, tarifa,
   rendimento: o que o extrato tem e não é lançamento. Sem isso o saldo nunca
   bate com o banco.

### Fase 4 — Plano de contas e centro de custo

Categorias em árvore, por natureza, e centros de custo. Entram como campos
opcionais do lançamento e como filtros da tela.

### Fase 5 — Auditoria e ajustes

Trilha de quem fez o quê e quando, descontos, juros e multa separados na baixa,
e recorrência genérica além dos contratos. Juros e multa **automáticos** sobre o
vencido dependem da taxa, que ainda é decisão em aberto no `DEPOIS.md`.

## O modelo a que o módulo chega

Escrito como o contrato TypeScript que o front recebe. No código, ele não é
escrito à mão: sai do `openapi.json`, que sai da API (decisão Q19). A fase de
cada campo está ao lado.

```ts
type Natureza = "Receber" | "Pagar";                          // fase 2
type Situacao = "Aberto" | "Pago" | "Cancelado" | "Renegociado";

interface Lancamento {
  id: string;
  natureza: Natureza;                                           // fase 2
  pessoaId: string;             // cliente ao receber, fornecedor ao pagar
  contratoId?: string;          // quando veio de recorrência de contrato
  descricao: string;
  competencia: { ano: number; mes: number };
  vencimento: string;           // data ISO
  valor: number;                // valor original: nunca reescrito
  situacao: Situacao;

  parcelamentoId?: string;                                      // fase 1
  parcela?: { numero: number; total: number };                  // fase 1
  renegociadoDeId?: string;     // nas parcelas que substituem  // fase 1

  baixa?: {
    pagoEm: string;
    valorPago: number;          // o que entrou ou saiu de fato
    origem: "manual" | "lote" | "cobranca";
    contaId?: string;                                           // fase 3
    desconto?: number;                                          // fase 5
    juros?: number;                                             // fase 5
    multa?: number;                                             // fase 5
  };

  cancelamento?: { motivo: string };
  cobranca?: { id: string; url: string };

  categoriaId?: string;                                         // fase 4
  centroDeCustoId?: string;                                     // fase 4

  criadoEm: string;
  atualizadoEm: string;
  criadoPor?: string;                                           // fase 5
  atualizadoPor?: string;                                       // fase 5
}

interface Renegociacao {                                        // fase 1
  id: string;
  origemId: string;             // o título que foi substituído
  juros: number;
  multa: number;
  desconto: number;
  motivo: string;
  novasParcelas: string[];
}

interface ContaBancaria {                                       // fase 3
  id: string;
  nome: string;
  saldoInicial: number;
  saldoInicialEm: string;
}

interface MovimentoDeConta {                                    // fase 3
  id: string;
  contaId: string;
  lancamentoId?: string;
  data: string;
  valor: number;                // positivo entra, negativo sai
}

interface Categoria {                                           // fase 4
  id: string;
  nome: string;
  natureza: Natureza;
  paiId?: string;
}

interface EventoDeAuditoria {                                   // fase 5
  id: string;
  lancamentoId: string;
  acao: string;
  antes: unknown;
  depois: unknown;
  usuarioId: string;
  em: string;
}
```

As regras que o modelo carrega, e que valem em todas as fases:

- **O valor original nunca é reescrito.** O que muda na baixa fica na baixa.
- **Renegociar não edita título.** O original vira `Renegociado` e as parcelas
  novas apontam para ele. `Renegociado` continua ocupando a competência no
  índice único, que só libera cancelados: sem isso, gerar mensalidades cobraria
  de novo um mês já renegociado.
- **Nada se apaga.** Cancelar exige motivo e mantém a linha.
- **Parcelas somam exatamente o total.** Os centavos que sobram da divisão vão
  para a primeira parcela, e o vencimento de cada uma parte da primeira data,
  não da parcela anterior: 31 de janeiro vira 28 de fevereiro e 31 de março, e
  não 28 de março.
- **Todo total vem do banco.**

## Os componentes da tela

A tela segue o que as outras listagens já fazem: recorte na URL, ordenação no
banco, cartão no celular.

```
app/(app)/lancamentos/page.tsx     a tela: consultas, recorte na URL, orquestração
  ResumoFinanceiro                 cards: em aberto, vencido, recebido; saldo na fase 3
  FiltrosDeLancamentos             período, situação, pessoa; categoria na fase 4
  TabelaDeLancamentos              tabela e cartões, com seleção e ordenação
    TarjaDeSituacao                pago, vencido, vence hoje, a vencer
    MenuDeAcoes                    por linha: baixar, cobrar, renegociar, cancelar, estornar
  BarraDeSelecao                   ações em lote: baixar em lote
  GavetaDeLancamento               lançar, com parcelamento
  GavetaDeRenegociacao             título original, acréscimos e parcelas novas
  DialogoDeBaixaEmLote             data da baixa, total, e a conta na fase 3
  FluxoDeCaixa                     fase 3: projetado contra realizado
```

Três peças saem das telas existentes em vez de nascerem de novo: a seleção e a
barra de ações de Pessoas, e o cabeçalho ordenável de `componentes/tabela.tsx`.
É a segunda vez que a seleção aparece, que é a hora que a Q20 marca para
extrair.

Cores de situação, com texto sempre junto e nunca só cor: verde para pago,
vermelho para vencido, âmbar para vence hoje, cinza para a vencer.
