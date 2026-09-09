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

    /* ------------------------------------------------------------- apoio */

    private async Task CriarContrato(
        HttpClient cliente,
        decimal valor,
        int dia,
        SituacaoContrato situacao = SituacaoContrato.Ativo,
        DateOnly? inicio = null,
        DateOnly? fim = null)
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

        var contrato = await cliente.PostAsJsonAsync("/contratos", new DadosDeContrato(
            clienteCriado!.Id, "Honorários contábeis", valor, dia,
            inicio ?? new DateOnly(2025, 1, 1), fim, situacao, string.Empty), Json);
        contrato.EnsureSuccessStatusCode();
    }

    private async Task<ResultadoDaGeracao> Gerar(HttpClient cliente, int ano, int mes)
    {
        var resposta = await cliente.PostAsJsonAsync("/contratos/gerar-mensalidades",
            new PedidoDeGeracao(ano, mes, null), Json);
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<ResultadoDaGeracao>(Json))!;
    }

    private static async Task<ResumoDeRecebiveis> Listar(HttpClient cliente) =>
        (await cliente.GetFromJsonAsync<ResumoDeRecebiveis>("/recebiveis", Json))!;

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
