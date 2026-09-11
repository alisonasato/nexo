using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;
using Npgsql;

namespace Nexo.Api.Testes;

/// <summary>
/// Contrato, mensalidade, recebível e baixa, pela borda HTTP.
///
/// É a parte do sistema onde o erro custa dinheiro de verdade — cobrar duas
/// vezes, baixar duas vezes, perder uma baixa —, então é onde os testes valem
/// mais.
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class CicloDoDinheiro(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Gerar_a_mesma_competencia_duas_vezes_nao_cobra_o_cliente_em_dobro()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        await CriarContrato(cliente, valor: 850m, dia: 10);

        var primeira = await Gerar(cliente, 2026, 3);
        var segunda = await Gerar(cliente, 2026, 3);

        Assert.Equal(1, primeira.Geradas);

        /*
         * O segundo clique não gera nada e não estoura. É o caso real: o fim
         * do mês é corrido, alguém clica de novo por dúvida, e o sistema
         * precisa responder "já está feito" em vez de duplicar a cobrança.
         */
        Assert.Equal(0, segunda.Geradas);
        Assert.Equal(1, segunda.Ignoradas);

        var recebiveis = await Listar(cliente);
        Assert.Single(recebiveis.Itens);
        Assert.Equal(850m, recebiveis.TotalEmAberto);
    }

    [Fact]
    public async Task Dois_cliques_ao_mesmo_tempo_geram_uma_mensalidade_so()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        await CriarContrato(cliente, valor: 640m, dia: 20);

        /*
         * Duas gerações disparadas sem esperar uma pela outra.
         *
         * Ele verifica o que se vê de fora: nenhuma das duas estoura e sobra
         * uma mensalidade só. Não prova qual das duas defesas agiu — derrubando
         * o índice único, este teste continua passando, porque as requisições
         * acabam se serializando e a checagem em código já resolve.
         *
         * A prova do índice está em Indice_unico_recusa_a_segunda_mensalidade,
         * que ataca o banco direto. As duas coisas são diferentes e as duas
         * precisam existir.
         */
        var primeira = cliente.PostAsJsonAsync("/contratos/gerar-mensalidades",
            new PedidoDeGeracao(2026, 8, null), Json);
        var segunda = cliente.PostAsJsonAsync("/contratos/gerar-mensalidades",
            new PedidoDeGeracao(2026, 8, null), Json);

        var respostas = await Task.WhenAll(primeira, segunda);

        /* Nenhuma das duas pode estourar na cara de quem clicou. */
        Assert.All(respostas, resposta => Assert.Equal(HttpStatusCode.OK, resposta.StatusCode));

        var recebiveis = await Listar(cliente);
        Assert.Single(recebiveis.Itens);
        Assert.Equal(640m, recebiveis.TotalEmAberto);
    }

    [Fact]
    public async Task Indice_unico_recusa_a_segunda_mensalidade_da_mesma_competencia()
    {
        var conta = await Contas.Criar(banco, _aplicacao);

        var pessoaId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();
        var contratoId = Guid.NewGuid();

        await using (var contexto = banco.Criar(conta.TenantId))
        {
            contexto.Pessoas.Add(new Pessoa
            {
                Id = pessoaId, TenantId = conta.TenantId, Tipo = TipoPessoa.Juridica,
                Nome = "Cliente do índice", Documento = CnpjValido(),
            });
            contexto.Clientes.Add(new Cliente
            {
                Id = clienteId, TenantId = conta.TenantId, PessoaId = pessoaId, Codigo = "C-9001",
            });
            contexto.Contratos.Add(new Contrato
            {
                Id = contratoId, TenantId = conta.TenantId, ClienteId = clienteId,
                Codigo = "C9001", Descricao = "Honorários", Valor = 100m,
                DiaDeVencimento = 10, InicioDaVigencia = new DateOnly(2025, 1, 1),
            });
            await contexto.SaveChangesAsync();
        }

        Recebivel Mensalidade() => new()
        {
            Id = Guid.NewGuid(), TenantId = conta.TenantId, ClienteId = clienteId,
            ContratoId = contratoId, CompetenciaAno = 2026, CompetenciaMes = 9,
            Descricao = "Honorários", Valor = 100m, Vencimento = new DateOnly(2026, 9, 10),
        };

        await using (var contexto = banco.Criar(conta.TenantId))
        {
            contexto.Recebiveis.Add(Mensalidade());
            await contexto.SaveChangesAsync();
        }

        /*
         * A segunda gravação vem de outra conexão, sem passar por checagem
         * nenhuma da aplicação — é o cenário de duas transações concorrentes,
         * reproduzido de forma determinística. Quem recusa é o banco.
         */
        await using (var contexto = banco.Criar(conta.TenantId))
        {
            contexto.Recebiveis.Add(Mensalidade());

            var erro = await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync());
            var causa = Assert.IsType<PostgresException>(erro.InnerException);
            Assert.Equal("23505", causa.SqlState);
        }
    }

    [Fact]
    public async Task Contrato_suspenso_nao_gera_mensalidade()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        await CriarContrato(cliente, valor: 500m, dia: 5, situacao: SituacaoContrato.Suspenso);

        var resultado = await Gerar(cliente, 2026, 3);

        Assert.Equal(0, resultado.Geradas);
        Assert.Equal(1, resultado.ForaDeVigencia);
        Assert.Empty((await Listar(cliente)).Itens);
    }

    [Fact]
    public async Task Contrato_encerrado_antes_da_competencia_nao_gera()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        await CriarContrato(cliente, valor: 500m, dia: 5,
            inicio: new DateOnly(2025, 1, 1), fim: new DateOnly(2026, 1, 31));

        var resultado = await Gerar(cliente, 2026, 3);

        Assert.Equal(0, resultado.Geradas);
        Assert.Equal(1, resultado.ForaDeVigencia);
    }

    [Fact]
    public async Task Vencimento_no_dia_31_cai_no_ultimo_dia_de_fevereiro()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        await CriarContrato(cliente, valor: 300m, dia: 31);

        await Gerar(cliente, 2026, 2);

        /*
         * Guardar o dia e derivar a data é o que permite isto. Se a data
         * inteira fosse cadastrada, alguém teria que cadastrar "dia 31, exceto
         * em fevereiro" — e não cadastraria.
         */
        var recebivel = Assert.Single((await Listar(cliente)).Itens);
        Assert.Equal(new DateOnly(2026, 2, 28), recebivel.Vencimento);
    }

    [Fact]
    public async Task Baixar_registra_o_valor_que_entrou_e_nao_o_que_era_devido()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        await CriarContrato(cliente, valor: 1000m, dia: 10);
        await Gerar(cliente, 2026, 4);

        var recebivel = Assert.Single((await Listar(cliente)).Itens);

        // Desconto combinado: entrou menos do que a cobrança dizia.
        var resposta = await cliente.PostAsJsonAsync(
            $"/recebiveis/{recebivel.Id}/baixar",
            new DadosDaBaixa(950m, new DateOnly(2026, 4, 12)), Json);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        var resumo = await Listar(cliente);
        var baixado = Assert.Single(resumo.Itens);

        Assert.Equal(SituacaoRecebivel.Pago, baixado.Situacao);
        Assert.Equal(1000m, baixado.Valor);
        Assert.Equal(950m, baixado.ValorPago);
        Assert.Equal(new DateOnly(2026, 4, 12), baixado.PagoEm);
        Assert.Equal(OrigensDeBaixa.Manual, baixado.OrigemDaBaixa);

        // O resumo conta o que entrou, não o que era devido.
        Assert.Equal(950m, resumo.TotalRecebido);
        Assert.Equal(0m, resumo.TotalEmAberto);
    }

    [Fact]
    public async Task Baixar_duas_vezes_e_recusado_em_vez_de_sobrescrever()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        await CriarContrato(cliente, valor: 400m, dia: 10);
        await Gerar(cliente, 2026, 5);

        var recebivel = Assert.Single((await Listar(cliente)).Itens);

        await cliente.PostAsJsonAsync($"/recebiveis/{recebivel.Id}/baixar",
            new DadosDaBaixa(400m, new DateOnly(2026, 5, 10)), Json);

        var repetida = await cliente.PostAsJsonAsync($"/recebiveis/{recebivel.Id}/baixar",
            new DadosDaBaixa(999m, new DateOnly(2026, 5, 20)), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, repetida.StatusCode);

        /*
         * E o valor da primeira baixa continua lá. Sobrescrever apagaria
         * exatamente o dado que alguém vai procurar quando a conciliação não
         * fechar.
         */
        var atual = Assert.Single((await Listar(cliente)).Itens);
        Assert.Equal(400m, atual.ValorPago);
        Assert.Equal(new DateOnly(2026, 5, 10), atual.PagoEm);
    }

    [Fact]
    public async Task Estornar_devolve_o_recebivel_para_aberto()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        await CriarContrato(cliente, valor: 700m, dia: 15);
        await Gerar(cliente, 2026, 6);

        var recebivel = Assert.Single((await Listar(cliente)).Itens);

        await cliente.PostAsJsonAsync($"/recebiveis/{recebivel.Id}/baixar",
            new DadosDaBaixa(700m, null), Json);

        var estorno = await cliente.PostAsync($"/recebiveis/{recebivel.Id}/estornar", null);
        Assert.Equal(HttpStatusCode.OK, estorno.StatusCode);

        var resumo = await Listar(cliente);
        var voltou = Assert.Single(resumo.Itens);

        Assert.Equal(SituacaoRecebivel.Aberto, voltou.Situacao);
        Assert.Null(voltou.ValorPago);
        Assert.Null(voltou.PagoEm);
        Assert.Equal(700m, resumo.TotalEmAberto);
    }

    [Fact]
    public async Task Um_escritorio_nao_ve_os_recebiveis_do_outro()
    {
        var clienteA = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var clienteB = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        await CriarContrato(clienteA, valor: 1200m, dia: 10);
        await Gerar(clienteA, 2026, 7);

        var deB = await Listar(clienteB);

        Assert.Empty(deB.Itens);
        Assert.Equal(0m, deB.TotalEmAberto);
    }

    [Fact]
    public void O_codigo_do_contrato_segue_o_maior_usado_e_nao_a_quantidade()
    {
        // Regra pura: vale a pena provar sem passar pelo banco.
        Assert.Equal("C0001", Codigos.Proximo("C", []));
        Assert.Equal("C0002", Codigos.Proximo("C", ["C0001"]));

        /*
         * O caso que a contagem erraria: dez contratos criados, os primeiros
         * apagados. Contar devolveria C0003, que já existe.
         */
        Assert.Equal("C0011", Codigos.Proximo("C", ["C0009", "C0010"]));
    }

    [Fact]
    public async Task Cancelar_libera_a_competencia_para_gerar_de_novo()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var contratoId = await CriarContrato(cliente, valor: 500m, dia: 10);

        await Gerar(cliente, 2026, 10);
        var errada = Assert.Single((await Listar(cliente)).Itens);
        Assert.Equal(500m, errada.Valor);

        /*
         * O erro comum: o valor do contrato estava errado. Corrige-se o
         * contrato, mas a mensalidade ja gerada continua com o valor velho.
         * Antes do cancelamento isso nao tinha conserto — a competencia ficava
         * ocupada para sempre por um recebivel errado.
         */
        await cliente.PutAsJsonAsync($"/contratos/{contratoId}", new DadosDeContrato(
            await ClienteDoContrato(cliente, contratoId),
            "Honorários contábeis", 780m, 10,
            new DateOnly(2025, 1, 1), null, SituacaoContrato.Ativo, string.Empty), Json);

        var cancelamento = await cliente.PostAsJsonAsync(
            $"/recebiveis/{errada.Id}/cancelar",
            new DadosDoCancelamento("Valor do contrato estava errado"), Json);
        Assert.Equal(HttpStatusCode.OK, cancelamento.StatusCode);

        var denovo = await Gerar(cliente, 2026, 10);
        Assert.Equal(1, denovo.Geradas);

        var abertos = (await Listar(cliente)).Itens
            .Where(item => item.Situacao == SituacaoRecebivel.Aberto)
            .ToList();

        var certa = Assert.Single(abertos);
        Assert.Equal(780m, certa.Valor);
    }

    [Fact]
    public async Task O_cancelado_sai_da_conta_mas_nao_do_historico()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        await CriarContrato(cliente, valor: 300m, dia: 10);
        await Gerar(cliente, 2026, 11);

        var recebivel = Assert.Single((await Listar(cliente)).Itens);

        await cliente.PostAsJsonAsync($"/recebiveis/{recebivel.Id}/cancelar",
            new DadosDoCancelamento("Cliente encerrou o contrato antes"), Json);

        var resumo = await Listar(cliente);

        // Some da conta...
        Assert.Equal(0m, resumo.TotalEmAberto);

        // ...e continua visível, com o motivo por escrito.
        var cancelado = Assert.Single(resumo.Itens);
        Assert.Equal(SituacaoRecebivel.Cancelado, cancelado.Situacao);
        Assert.Equal("Cliente encerrou o contrato antes", cancelado.MotivoDoCancelamento);
    }

    [Fact]
    public async Task Cancelar_sem_motivo_e_recusado()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        await CriarContrato(cliente, valor: 300m, dia: 10);
        await Gerar(cliente, 2026, 12);

        var recebivel = Assert.Single((await Listar(cliente)).Itens);

        var resposta = await cliente.PostAsJsonAsync($"/recebiveis/{recebivel.Id}/cancelar",
            new DadosDoCancelamento("   "), Json);

        // Cancelar tira dinheiro da conta; quem olhar depois vai perguntar por quê.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
    }

    [Fact]
    public async Task Cancelar_um_ja_baixado_e_recusado()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        await CriarContrato(cliente, valor: 300m, dia: 10);
        await Gerar(cliente, 2027, 1);

        var recebivel = Assert.Single((await Listar(cliente)).Itens);
        await cliente.PostAsJsonAsync($"/recebiveis/{recebivel.Id}/baixar",
            new DadosDaBaixa(300m, null), Json);

        var resposta = await cliente.PostAsJsonAsync($"/recebiveis/{recebivel.Id}/cancelar",
            new DadosDoCancelamento("Mudei de ideia"), Json);

        /*
         * Cancelar o que ja foi pago apagaria a entrada de dinheiro da conta
         * sem devolver nada a ninguem.
         */
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
    }

    [Fact]
    public async Task Estornar_um_cancelado_e_recusado()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        await CriarContrato(cliente, valor: 300m, dia: 10);
        await Gerar(cliente, 2027, 2);

        var recebivel = Assert.Single((await Listar(cliente)).Itens);
        await cliente.PostAsJsonAsync($"/recebiveis/{recebivel.Id}/cancelar",
            new DadosDoCancelamento("Gerado por engano"), Json);

        var estorno = await cliente.PostAsync($"/recebiveis/{recebivel.Id}/estornar", null);

        /*
         * Estorno desfaz baixa. Sobre um cancelado, ressuscitaria a cobranca —
         * e poderia colidir com a mensalidade gerada no lugar dela.
         */
        Assert.Equal(HttpStatusCode.UnprocessableEntity, estorno.StatusCode);
    }

    [Fact]
    public async Task Os_totais_somam_o_periodo_inteiro_e_nao_a_pagina()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        await CriarContrato(cliente, valor: 100m, dia: 10);
        await CriarContrato(cliente, valor: 200m, dia: 10);
        await CriarContrato(cliente, valor: 300m, dia: 10);

        await Gerar(cliente, 2027, 3);

        var primeiraPagina = await cliente.GetFromJsonAsync<PaginaDeRecebiveis>(
            "/recebiveis?ano=2027&mes=3&pagina=1&tamanho=2", Json);

        /*
         * Duas linhas na página, três no período. Se o total viesse da página,
         * ele diria 300 — um número errado com cara de certo, que ninguém
         * desconfiaria de olhar.
         */
        Assert.Equal(2, primeiraPagina!.Itens.Count);
        Assert.Equal(3, primeiraPagina.Total);
        Assert.Equal(600m, primeiraPagina.TotalEmAberto);
    }

    [Fact]
    public async Task A_segunda_pagina_traz_o_que_sobrou()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        await CriarContrato(cliente, valor: 100m, dia: 10);
        await CriarContrato(cliente, valor: 200m, dia: 10);
        await CriarContrato(cliente, valor: 300m, dia: 10);

        await Gerar(cliente, 2027, 4);

        var segunda = await cliente.GetFromJsonAsync<PaginaDeRecebiveis>(
            "/recebiveis?ano=2027&mes=4&pagina=2&tamanho=2", Json);

        Assert.Single(segunda!.Itens);
        Assert.Equal(3, segunda.Total);
        Assert.Equal(2, segunda.Pagina);
    }

    [Fact]
    public async Task Os_totais_seguem_a_competencia_e_nao_o_filtro_de_situacao()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        await CriarContrato(cliente, valor: 400m, dia: 10);
        await CriarContrato(cliente, valor: 600m, dia: 10);
        await Gerar(cliente, 2027, 5);

        var doMes = await cliente.GetFromJsonAsync<PaginaDeRecebiveis>(
            "/recebiveis?ano=2027&mes=5", Json);
        var umDeles = doMes!.Itens.First();

        await cliente.PostAsJsonAsync($"/recebiveis/{umDeles.Id}/baixar",
            new DadosDaBaixa(umDeles.Valor, null), Json);

        var soPagos = await cliente.GetFromJsonAsync<PaginaDeRecebiveis>(
            "/recebiveis?ano=2027&mes=5&situacao=Pago", Json);

        /*
         * A lista mostra a fatia escolhida — um pago. Os totais mostram o mês
         * inteiro, porque "quanto ainda falta entrar em maio" é a pergunta que
         * o escritório faz enquanto olha o que já entrou.
         */
        Assert.Single(soPagos!.Itens);
        Assert.Equal(1000m - umDeles.Valor, soPagos.TotalEmAberto);
        Assert.Equal(umDeles.Valor, soPagos.TotalRecebido);
    }

    [Fact]
    public async Task O_total_mensal_dos_contratos_nao_encolhe_com_a_pagina()
    {
        var cliente = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        await CriarContrato(cliente, valor: 100m, dia: 10);
        await CriarContrato(cliente, valor: 250m, dia: 10);
        await CriarContrato(cliente, valor: 400m, dia: 10);

        var pagina = await cliente.GetFromJsonAsync<PaginaDeContratos>(
            "/contratos?pagina=1&tamanho=1", Json);

        Assert.Single(pagina!.Itens);
        Assert.Equal(3, pagina.Total);

        // Quanto o escritório fatura por mês não muda com o tamanho da página.
        Assert.Equal(750m, pagina.TotalMensalAtivo);
    }

    /* ------------------------------------------------------------- apoio */

    private async Task<Guid> CriarContrato(
        HttpClient cliente,
        decimal valor,
        int dia,
        SituacaoContrato situacao = SituacaoContrato.Ativo,
        DateOnly? inicio = null,
        DateOnly? fim = null)
    {
        var clienteId = await CriarCliente(cliente);

        var contrato = await cliente.PostAsJsonAsync("/contratos", new DadosDeContrato(
            clienteId, "Honorários contábeis", valor, dia,
            inicio ?? new DateOnly(2025, 1, 1), fim, situacao, string.Empty), Json);
        contrato.EnsureSuccessStatusCode();

        var criado = await contrato.Content.ReadFromJsonAsync<ContratoDetalhado>(Json);
        return criado!.Id;
    }

    /// <summary>O cliente de um contrato, para poder alterá-lo sem inventar o vínculo.</summary>
    private static async Task<Guid> ClienteDoContrato(HttpClient cliente, Guid contratoId)
    {
        var contrato = await cliente.GetFromJsonAsync<ContratoDetalhado>($"/contratos/{contratoId}", Json);
        return contrato!.ClienteId;
    }

    private async Task<ResultadoDaGeracao> Gerar(HttpClient cliente, int ano, int mes)
    {
        var resposta = await cliente.PostAsJsonAsync("/contratos/gerar-mensalidades",
            new PedidoDeGeracao(ano, mes, null), Json);
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<ResultadoDaGeracao>(Json))!;
    }

    /* --------------------------------------- geração por seleção */

    [Fact]
    public async Task Gerar_com_a_lista_de_ids_so_atinge_os_escolhidos()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        var escolhido = await CriarContrato(http, 500m, 10);
        await CriarContrato(http, 900m, 10);
        await CriarContrato(http, 700m, 10);

        var resposta = await http.PostAsJsonAsync("/contratos/gerar-mensalidades",
            new PedidoDeGeracao(2026, 3, [escolhido]), Json);

        var resultado = await resposta.Content.ReadFromJsonAsync<ResultadoDaGeracao>(Json);
        Assert.Equal(1, resultado!.Geradas);

        var recebiveis = await Listar(http);
        Assert.Equal(500m, Assert.Single(recebiveis.Itens).Valor);
    }

    [Fact]
    public async Task Lista_vazia_nao_gera_nada_e_lista_nula_gera_tudo()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        await CriarContrato(http, 400m, 10);
        await CriarContrato(http, 600m, 10);

        /*
         * A distinção entre lista vazia e lista ausente é o que a tela usa para
         * dizer "só estes" e "todos" com o mesmo botão. Trocar uma pela outra
         * geraria a competência inteira quando alguém quis gerar nada, e isso
         * não se desfaz sem cancelar recebível por recebível.
         */
        var vazia = await http.PostAsJsonAsync("/contratos/gerar-mensalidades",
            new PedidoDeGeracao(2026, 4, []), Json);

        Assert.Equal(0, (await vazia.Content.ReadFromJsonAsync<ResultadoDaGeracao>(Json))!.Geradas);
        Assert.Empty((await Listar(http)).Itens);

        var nula = await http.PostAsJsonAsync("/contratos/gerar-mensalidades",
            new PedidoDeGeracao(2026, 4, null), Json);

        Assert.Equal(2, (await nula.Content.ReadFromJsonAsync<ResultadoDaGeracao>(Json))!.Geradas);
    }

    [Fact]
    public async Task Contrato_de_outro_tenant_na_lista_e_simplesmente_ignorado()
    {
        var httpA = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var httpB = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        var doA = await CriarContrato(httpA, 800m, 10);
        var doB = await CriarContrato(httpB, 300m, 10);

        /*
         * B manda o id do contrato de A junto com o seu. A política de RLS
         * filtra antes: para B, o contrato de A não existe, então não há o que
         * ignorar explicitamente — ele nunca entra na lista de candidatos.
         */
        var resposta = await httpB.PostAsJsonAsync("/contratos/gerar-mensalidades",
            new PedidoDeGeracao(2026, 5, [doA, doB]), Json);

        Assert.Equal(1, (await resposta.Content.ReadFromJsonAsync<ResultadoDaGeracao>(Json))!.Geradas);

        Assert.Equal(300m, Assert.Single((await Listar(httpB)).Itens).Valor);
        Assert.Empty((await Listar(httpA)).Itens);
    }

    /* ------------------------------------------------ cobrança avulsa */

    [Fact]
    public async Task Avulso_entra_na_conta_do_mes_sem_contrato_por_tras()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var clienteId = await CriarCliente(http);

        var resposta = await http.PostAsJsonAsync("/recebiveis",
            Avulso(clienteId, "Declaração de IRPF 2026", 850m), Json);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);

        var resumo = await Listar(http);
        var recebivel = Assert.Single(resumo.Itens);

        Assert.Equal("Declaração de IRPF 2026", recebivel.Descricao);
        Assert.Equal(SituacaoRecebivel.Aberto, recebivel.Situacao);

        /* O avulso conta no total do mês como qualquer mensalidade contaria. */
        Assert.Equal(850m, resumo.TotalEmAberto);
    }

    [Fact]
    public async Task Dois_avulsos_podem_dividir_a_mesma_competencia()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var clienteId = await CriarCliente(http);

        var primeira = await http.PostAsJsonAsync("/recebiveis",
            Avulso(clienteId, "Certidão negativa", 120m), Json);
        var segunda = await http.PostAsJsonAsync("/recebiveis",
            Avulso(clienteId, "Alteração contratual", 400m), Json);

        /*
         * O índice único que impede cobrar a mensalidade duas vezes cobre
         * `ContratoId + competência`, e no Postgres duas linhas com NULL nessa
         * coluna não colidem. É o comportamento que se quer: a proteção existe
         * contra a máquina gerar duas vezes, não contra o escritório emitir
         * dois serviços no mesmo mês.
         */
        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Created, segunda.StatusCode);

        var resumo = await Listar(http);
        Assert.Equal(2, resumo.Itens.Count);
        Assert.Equal(520m, resumo.TotalEmAberto);
    }

    [Fact]
    public async Task Avulso_com_valor_zero_e_recusado()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var clienteId = await CriarCliente(http);

        var resposta = await http.PostAsJsonAsync("/recebiveis",
            Avulso(clienteId, "Serviço de graça", 0m), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);

        var corpo = await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>(Json);
        Assert.Equal("valor", Assert.Single(corpo!.Problemas).Campo);
    }

    [Fact]
    public async Task Avulso_para_cliente_de_outro_tenant_e_recusado()
    {
        var httpA = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var httpB = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));

        var doA = await CriarCliente(httpA);

        var resposta = await httpB.PostAsJsonAsync("/recebiveis",
            Avulso(doA, "Cobrança no cliente alheio", 999m), Json);

        /*
         * 422 dizendo "o cliente não existe", e é a verdade: sob a política de
         * RLS o cliente do outro escritório não está lá para ser encontrado.
         */
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);

        var corpo = await resposta.Content.ReadFromJsonAsync<RespostaComProblemas>(Json);
        Assert.Equal("clienteId", Assert.Single(corpo!.Problemas).Campo);

        Assert.Empty((await Listar(httpA)).Itens);
    }

    [Fact]
    public async Task Avulso_se_baixa_como_qualquer_recebivel()
    {
        var http = await Contas.Entrar(_aplicacao, await Contas.Criar(banco, _aplicacao));
        var clienteId = await CriarCliente(http);

        var criado = await http.PostAsJsonAsync("/recebiveis",
            Avulso(clienteId, "Abertura de empresa", 1_200m), Json);
        var avulso = await criado.Content.ReadFromJsonAsync<RecebivelNaLista>(Json);

        /* Sem caminho próprio de baixa: entra na mesma máquina do resto. */
        var baixa = await http.PostAsJsonAsync($"/recebiveis/{avulso!.Id}/baixar",
            new DadosDaBaixa(1_200m, new DateOnly(2026, 2, 5)), Json);

        Assert.Equal(HttpStatusCode.OK, baixa.StatusCode);

        var resumo = await Listar(http);
        Assert.Equal(SituacaoRecebivel.Pago, Assert.Single(resumo.Itens).Situacao);
        Assert.Equal(1_200m, resumo.TotalRecebido);
        Assert.Equal(0m, resumo.TotalEmAberto);
    }

    private static DadosDoAvulso Avulso(Guid clienteId, string descricao, decimal valor) =>
        new(clienteId, descricao, valor, new DateOnly(2026, 2, 10), 2026, 1);

    /* --------------------------------------------------------- apoio */

    private static async Task<PaginaDeRecebiveis> Listar(HttpClient cliente) =>
        (await cliente.GetFromJsonAsync<PaginaDeRecebiveis>("/recebiveis", Json))!;

    /// <summary>Uma pessoa nova e o vínculo de cliente, pela borda HTTP.</summary>
    private static async Task<Guid> CriarCliente(HttpClient cliente)
    {
        var pessoa = await cliente.PostAsJsonAsync("/pessoas", new DadosDePessoa(
            TipoPessoa.Juridica, "Cliente " + Guid.NewGuid().ToString("N")[..8], string.Empty,
            CnpjValido(), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            null, string.Empty, true), Json);
        pessoa.EnsureSuccessStatusCode();
        var criada = await pessoa.Content.ReadFromJsonAsync<PessoaDetalhada>(Json);

        var vinculo = await cliente.PostAsJsonAsync("/clientes", new DadosDeCliente(
            criada!.Id, RegimeTributario.SimplesNacional, "Responsável", string.Empty, true), Json);
        vinculo.EnsureSuccessStatusCode();

        var clienteCriado = await vinculo.Content.ReadFromJsonAsync<ClienteNaLista>(Json);
        return clienteCriado!.Id;
    }

    /// <summary>Um CNPJ novo com dígitos verificadores certos.</summary>
    private static string CnpjValido()
    {
        var base12 = Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString();

        int[] primeiroPeso = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] segundoPeso = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

        var primeiro = Digito(base12, primeiroPeso);
        var segundo = Digito(base12 + primeiro, segundoPeso);
        return base12 + primeiro + segundo;

        static int Digito(string numero, int[] pesos)
        {
            var soma = numero.Select((caractere, indice) => (caractere - '0') * pesos[indice]).Sum();
            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }
    }
}
