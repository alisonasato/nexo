# Backup e restauração

Backup que nunca foi restaurado é hipótese, não backup. Este documento existe
porque a restauração foi exercitada de verdade, num banco com dados, e o
percurso revelou duas armadilhas que não aparecem sozinhas.

Tudo aqui foi verificado num Postgres 17 local, com 12 pessoas, 6 clientes, 6
contratos e 36 recebíveis. O que **não** deu para verificar está no fim.

## As duas armadilhas, primeiro

Quem lê só o começo precisa levar estas duas.

### 1. O papel da aplicação não consegue fazer backup

`pg_dump` com o papel da aplicação **falha**, e a culpa é da proteção que a
gente quer:

```
pg_dump: error: consulta falhou: ERROR:  query would be affected by
row-level security policy for table "clientes"
```

`FORCE ROW LEVEL SECURITY` vale inclusive para o dono da tabela, e o `pg_dump`
se recusa a copiar uma tabela que a política filtraria — porque copiar parte
dela e chamar isso de backup seria pior.

**O backup precisa de um papel com `BYPASSRLS` ou superusuário.** Na Railway é
o papel administrativo que já existe (`postgres`), e não o `nexo_app` que a
aplicação usa.

E o detalhe que morde: **o arquivo é criado mesmo quando o comando falha.**
Ficaram 35 KB de dump incompleto no disco. Um script que só olhe se o arquivo
existe vai guardar lixo com cara de backup por meses.

### 2. Desligar o FORCE para conseguir o dump arruína o backup

A saída óbvia para a armadilha 1 é desligar o `FORCE` para o dump passar. Ela
funciona, e é justamente por isso que é perigosa.

Verificado: com o `FORCE` desligado na origem, o dump sai inteiro e os dados
restauram perfeitos — contagem e conteúdo idênticos nas quatro tabelas. Mas o
banco restaurado fica assim:

| | Origem | Restaurado |
|---|---|---|
| Políticas de RLS | 5 | 5 |
| Tabelas com RLS ligada | 5 | 5 |
| **Tabelas com FORCE** | **5** | **0** |

As políticas estão todas lá. Passa em qualquer conferência superficial. E o
isolamento não existe mais, porque o dono das tabelas é a aplicação:

```
Origem, sem dizer o tenant:       0 pessoas visíveis
Restaurado, sem dizer o tenant:  12 pessoas visíveis
```

Um escritório enxergaria o dado do outro, e nada acusaria. É o mesmo erro sem
sintoma que a conferência da subida existe para pegar — e ela **pegaria** este
caso, porque olha o papel, não o FORCE. Então não confie nela aqui: confira o
FORCE depois de toda restauração.

## Fazer o backup

Com o papel administrativo, e a URL pública do banco:

```bash
pg_dump "URL_ADMINISTRATIVA_DO_BANCO" -Fc -f nexo-2026-09-11.dump
```

O formato `-Fc` é comprimido e permite restaurar tabelas soltas. Confira o
código de saída, não a existência do arquivo:

```bash
pg_dump "URL" -Fc -f nexo.dump && echo "backup ok" || echo "BACKUP FALHOU"
```

## Restaurar

Num banco vazio:

```bash
pg_restore -d "URL_DO_BANCO_DESTINO" nexo.dump
```

Num banco que já tem o esquema, limpando antes:

```bash
psql "URL_DO_BANCO_DESTINO" -c "DROP SCHEMA public CASCADE; CREATE SCHEMA public;"
pg_restore -d "URL_DO_BANCO_DESTINO" nexo.dump
```

## Conferir a restauração

**Isto não é opcional**, pela armadilha 2. Rode
[ferramentas/conferir-restauracao.sql](ferramentas/conferir-restauracao.sql) no
banco restaurado: ele checa as 14 tabelas, as 5 políticas, as 5 marcas de
`FORCE`, o índice único parcial, e termina dizendo `RESTAURACAO OK` ou listando
o que falta.

Depois, compare os dados com a origem se ela ainda existir. Contagem por si só
não prova nada sobre o conteúdo:

```sql
SELECT md5(string_agg(t::text, '|' ORDER BY t::text)) FROM pessoas t;
```

Verificado assim: as quatro tabelas de negócio deram a mesma soma dos dois
lados.

## O que já está verificado

- **Fidelidade do esquema.** Dump e restauração preservaram as 14 tabelas, as 5
  políticas, as 5 marcas de `FORCE` e o índice único parcial
  `WHERE (situacao <> 3)` — que é o que impede cobrar a mesma mensalidade duas
  vezes, e cuja perda silenciosa seria cara.
- **Fidelidade dos dados.** Contagem e soma de verificação idênticas em pessoas,
  clientes, contratos e recebíveis.
- **As duas armadilhas acima**, ambas reproduzidas de propósito.

## O que não foi verificado

- **O backup automático da Railway.** Não foi possível conferir se está ligado,
  nem restaurar a partir de um. Isso depende de acesso ao painel e à URL do
  banco de produção. **Confira no painel se o backup está ativo, e qual a
  frequência.**
- **Restauração de um banco grande.** O exercício foi com 61 linhas no total. O
  tempo de restauração com volume de verdade é outra conversa.
- **Ponto de recuperação no tempo.** Só há dump e restauração completos.
