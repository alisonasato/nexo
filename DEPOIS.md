# Depois

Este arquivo existe por causa da decisão Q27.

Não há primeiro usuário (Q23), então nada de fora empurra o escopo de volta ao
lugar. A única força que decide o que construir é a vontade — e a vontade de
quem gosta de construir sistema escolhe sempre construir mais. Foi assim que o
protótipo anterior ganhou precificação, comissão de vendedor e importação de
planilha **antes de ter onde gravar dado**.

**A regra:** enquanto a primeira entrega não estiver em produção, ideia nova
vira linha aqui. Nunca um commit.

Uma linha aqui não é uma promessa. É só a garantia de que a ideia não se perde
e não atrapalha.

---

## Deixado de fora da primeira entrega, de propósito

- Contas a pagar e caixa — o ciclo financeiro completo do escritório (era a
  opção 3 da Q24).
- Portal do cliente do escritório: documentos, boletos, solicitações (era a
  opção 3 da Q18, e é a primeira expansão natural).
- NFS-e por agregador (Q21). Conferir na hora a cobertura real do padrão
  nacional, que vem mudando essa conta.
- Migração dos dados do Cuca. Fora de escopo enquanto a vertical for outra.

## Herdado do protótipo, ainda sem dono

- `error.tsx` e tratamento de erro de rota.
- Visão em cartão para tabelas no celular.
- Seleção de linhas e painel de colunas na listagem de produtos.
- Comissão padrão por categoria de produto.
- Comissão fixa em reais, além do percentual.
- Unificar `Cliente.codigo` (`C-0001`) com `Contrato.codigo` (`C0001`).
- Geração de lançamento a partir da venda.

## Aberto pelos passos 1 e 2

- Trocar o Postgres portátil por Testcontainers, quando a máquina tiver Docker.
  Hoje ela não tem — sem Docker, sem WSL e sem administrador —, então o passo 2
  usou binários oficiais descompactados em `C:\dev\pgsql`. Os testes leem a
  conexão de `NEXO_TESTES_CONEXAO`, então a troca é de variável, não de código.
- Papel separado para a aplicação, sem ser dono das tabelas. Hoje `nexo` é dono
  e quem segura a política é o `FORCE ROW LEVEL SECURITY`. Em produção o certo
  é dono e aplicação serem papéis diferentes — o FORCE vira a segunda defesa,
  não a única. (O caso mais grave, superusuário, já é recusado na subida.)

## Aberto pelo passo 3

- Cadastro de cliente novo. Hoje existe o provisionamento do **primeiro**
  tenant, por configuração, e mais nada. Como um segundo escritório vira
  cliente do Nexo é um fluxo que ainda não foi desenhado.
- **Invalidar sessões abertas ao trocar a senha.** A troca existe, mas o token
  é autocontido: uma sessão aberta em outro navegador segue valendo por até
  oito horas. No dia em que a troca for por suspeita de vazamento, isso passa a
  importar — e a resposta é validar o carimbo de segurança do Identity a cada
  requisição, ao custo de uma consulta ao banco.
- **Recuperar senha esquecida.** Não existe. Hoje a saída é reprovisionar, o
  que exige banco vazio — ou seja, não é saída nenhuma. Depende de e-mail
  transacional, que o projeto ainda não tem.
- Convidar outra pessoa para o mesmo escritório. Um tenant tem um usuário só.
- Uma pessoa pertence a exatamente um tenant, e o e-mail é único no sistema
  inteiro. Quem trabalha em dois escritórios precisaria de duas contas com
  e-mails diferentes. É limitação conhecida, não descuido.
- Renovação de sessão. O token vale oito horas e acabou: não há refresh nem
  expiração deslizante, então quem passa do prazo entra de novo.
- Revogação. Como o token é autocontido, desligar um usuário no banco não
  invalida o token que ele já tem, até expirar. Se isso passar a importar,
  vira lista de revogados ou sessão com estado.
- Troca de empresa em tela. O `empresa_id` vai no token com a empresa padrão
  do usuário; trocar de estabelecimento sem sair e entrar ainda não existe.
- Papéis e permissões. O Identity já tem a tabela de papéis, e nada os usa:
  hoje quem entra pode tudo dentro do próprio tenant.

## Aberto pelo passo 4

- O motor de telas declarativas (Q20). Sai por extração quando a segunda ou
  terceira tela mostrar o que se repete — não antes.
- Consulta de CNPJ por API pública, para puxar razão social e endereço da
  Receita. O CEP já é consultado; o CNPJ ainda é digitado inteiro à mão.
- Guardar por um tempo o CEP já consultado. Hoje cada digitação sai para o
  ViaCEP, e um escritório cadastra vários clientes do mesmo prédio.
- Máscara no valor em dinheiro. O campo aceita `1.234,56` e `1234.56` na
  leitura, mas não se formata enquanto se digita como os outros agora fazem.
- CNPJ alfanumérico. A validação implementada é a numérica clássica;
  conferir a regra vigente antes de o primeiro cliente digitar um.
- Ordenar a listagem por outra coluna que não o nome.

## Aberto pelo passo 5

- **Cobrança automática pelo PSP.** Não é dívida técnica: é a decisão de
  qual PSP e a conta nele, que só você pode tomar. Detalhes em DECISOES.md.
- Gerar mensalidade só dos contratos selecionados. A API aceita a lista de
  ids; a tela sempre manda todos.
- Editar ou cancelar uma cobrança avulsa recém-lançada. Hoje o conserto de um
  valor digitado errado é cancelar e lançar de novo, o que funciona e deixa
  duas linhas no histórico onde bastaria uma.
- Reajuste de contrato por índice, e histórico de valores. Hoje mudar o valor
  reescreve o contrato, sem deixar rastro do que era antes.
- Juros e multa sobre o vencido. Hoje o vencido só aparece destacado.
- Corpo de requisição malformado devolve 400 cru, sem o formato de problemas
  que o resto da API usa.

## Aberto pela preparação para produção

- **Tabelas do Identity em PascalCase** (`AspNetUsers`), enquanto as de
  negócio são `snake_case`. O Identity fixa os nomes e a convenção não os
  alcança. O EF cita os identificadores, então nada quebra; incomoda quem
  escrever SQL à mão. Trocar é mais barato agora, antes de haver dado.
- **Corpo malformado devolve 400 cru**, fora do formato de problemas que o
  resto da API usa. Só acontece com cliente com defeito — o front é gerado
  do contrato —, e por isso ficou para depois.
- **Conferir o backup automático da Railway.** O procedimento de restauração
  está exercitado e documentado em BACKUP.md, mas com um dump feito à mão. Se o
  backup automático do PaaS está ligado, e se um arquivo dele restaura, depende
  de acesso ao painel — é passo seu, não meu.
- **Restauração com volume de verdade.** O exercício foi com 61 linhas. Quanto
  demora restaurar um banco de escritório cheio é outra conversa.
