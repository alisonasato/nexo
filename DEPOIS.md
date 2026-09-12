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

- Visão em cartão para tabelas no celular.
- Seleção de linhas e painel de colunas na listagem de produtos.
- Comissão padrão por categoria de produto.
- Comissão fixa em reais, além do percentual.
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
- **Recuperar senha pela tela, sem ajuda.** Existe a saída de emergência por
  variável de ambiente (ver IMPLANTACAO.md), que destrava quem perdeu a senha
  mas exige acesso ao painel do PaaS. O "esqueci minha senha" de verdade, que a
  própria pessoa resolve, depende de e-mail transacional — que o projeto ainda
  não tem, e que é a decisão que falta.
- Convidar outra pessoa para o mesmo escritório. Um tenant tem um usuário só.
- Uma pessoa pertence a exatamente um tenant, e o e-mail é único no sistema
  inteiro. Quem trabalha em dois escritórios precisaria de duas contas com
  e-mails diferentes. É limitação conhecida, não descuido.
- Renovação de sessão. O token vale oito horas e acabou: não há refresh nem
  expiração deslizante, então quem passa do prazo entra de novo.
- Desligar um usuário. O mecanismo de revogação já existe — a conferência do
  carimbo a cada requisição derruba a sessão no ato, e trocar o carimbo é uma
  linha (`UpdateSecurityStampAsync`). O que falta é a tela e a decisão de quem
  pode desligar quem, que só faz sentido junto com papéis e permissões.
- **Editar a empresa do próprio escritório.** Não existe: o endpoint de
  empresas só tem `GET`, nenhuma tela usa, e o provisionamento escreve razão
  social e CNPJ uma vez. Se o CNPJ foi digitado errado na variável de ambiente,
  não há conserto pela aplicação. É a falta mais concreta que a análise de
  centralização do cadastro encontrou, e é independente dela.
- Endereço e contato da empresa. `Empresa` não tem nenhum dos dois, e a NFS-e
  vai exigir os dois do estabelecimento.
- `Empresa` como vínculo com `Pessoa`, como os papéis agora são. A incoerência
  está analisada em DECISOES.md, com o argumento contra: `Empresa` é a espinha
  do isolamento, não um terceiro. A hora de mexer é junto com a NFS-e, que é o
  que cobra o endereço — e enquanto isso a migração encarece a cada escritório.
- `Usuario.Nome` é texto solto, e não um vínculo com `Pessoa`. Não paga nada
  enquanto houver um usuário por escritório; vale junto com convidar alguém.
- Troca de empresa em tela. O `empresa_id` vai no token com a empresa padrão
  do usuário; trocar de estabelecimento sem sair e entrar ainda não existe.
- Papéis e permissões. O Identity já tem a tabela de papéis, e nada os usa:
  hoje quem entra pode tudo dentro do próprio tenant.

## Aberto pelo passo 4

- O motor de telas declarativas (Q20). Sai por extração quando a segunda ou
  terceira tela mostrar o que se repete — não antes.
- CNPJ alfanumérico. A validação implementada é a numérica clássica;
  conferir a regra vigente antes de o primeiro cliente digitar um.
- Ordenar a listagem por outra coluna que não o nome.

## Aberto pelos papéis

- Dado por papel. Hoje o rótulo é só o rótulo: vendedor não tem comissão,
  fornecedor não tem condição de pagamento. Quando um papel precisar de campo
  próprio, ele ganha tabela própria pendurada na associativa — e não coluna
  nula em `pessoas`.
- Avisar antes de encerrar contrato ou cancelar cobrança de quem está prestes a
  sair. Hoje a recusa ao inativar diz o que falta fazer, mas quem chega pelo
  contrato não sabe que alguém está esperando aquilo para sumir do cadastro.

## Aberto pelo passo 5

- **Cobrança automática pelo PSP.** Não é dívida técnica: é a decisão de
  qual PSP e a conta nele, que só você pode tomar. Detalhes em DECISOES.md.
- Editar ou cancelar uma cobrança avulsa recém-lançada. Hoje o conserto de um
  valor digitado errado é cancelar e lançar de novo, o que funciona e deixa
  duas linhas no histórico onde bastaria uma.
- Reajuste de contrato por índice, e histórico de valores. Hoje mudar o valor
  reescreve o contrato, sem deixar rastro do que era antes.
- Juros e multa sobre o vencido. Hoje o vencido só aparece destacado.

## Aberto pela preparação para produção

- **Tabelas do Identity em PascalCase** (`AspNetUsers`), enquanto as de
  negócio são `snake_case`. O Identity fixa os nomes e a convenção não os
  alcança. O EF cita os identificadores, então nada quebra; incomoda quem
  escrever SQL à mão. A janela barata fechou: já há dado em produção, então a
  troca agora exige migração de verdade, e o ganho é só de gosto.
- **Conferir o backup automático da Railway.** O procedimento de restauração
  está exercitado e documentado em BACKUP.md, mas com um dump feito à mão. Se o
  backup automático do PaaS está ligado, e se um arquivo dele restaura, depende
  de acesso ao painel — é passo seu, não meu.
- **Restauração com volume de verdade.** O exercício foi com 61 linhas. Quanto
  demora restaurar um banco de escritório cheio é outra conversa.
