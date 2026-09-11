using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

namespace Nexo.Api.Testes;

/// <summary>
/// Ligar e desligar um cliente do escritório.
///
/// <para>
/// O que estes testes protegem não é o liga-desliga em si, que é trivial: é a
/// escolha de dar endereço próprio às duas ações em vez de deixar a tela
/// mandar um <c>PUT</c> com <c>ativo</c> trocado. O <c>PUT</c> reescreve o
/// vínculo inteiro, e a listagem não carrega <c>Observacoes</c> — a tela
/// apagaria a anotação sem que ninguém percebesse.
/// </para>
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class VinculoDeClientes(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private const string Anotacao = "Entrega o malote na segunda, sempre pela manhã.";

    public void Dispose() => _aplicacao.Dispose();

    [Fact]
    public async Task Inativar_tira_da_lista_sem_apagar_a_anotacao()
    {
        var conta = await Contas.Criar(banco, _aplicacao);
        var clienteId = await Vincular(conta.TenantId);
        var http = await Contas.Entrar(_aplicacao, conta);

        var resposta = await http.DeleteAsync($"/clientes/{clienteId}");
        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);

        var lista = await http.GetFromJsonAsync<List<ClienteNaLista>>("/clientes", Json);
        Assert.Empty(lista!);

        var comInativos = await http.GetFromJsonAsync<List<ClienteNaLista>>(
            "/clientes?incluirInativos=true", Json);
        Assert.False(Assert.Single(comInativos!).Ativo);

        /*
         * Esta é a asserção que carrega o peso.
         *
         * `Observacoes` não sai em nenhuma resposta, então só o banco pode
         * dizer se sobreviveu. Se um dia alguém trocar estes endpoints por um
         * PUT vindo da tela, é aqui que vai estourar — e não numa reclamação
         * de que "as anotações sumiram" seis meses depois.
         */
        Assert.Equal(Anotacao, await AnotacaoNoBanco(conta.TenantId, clienteId));
    }

    [Fact]
    public async Task Reativar_traz_de_volta_para_a_lista()
    {
        var conta = await Contas.Criar(banco, _aplicacao);
        var clienteId = await Vincular(conta.TenantId);
        var http = await Contas.Entrar(_aplicacao, conta);

        await http.DeleteAsync($"/clientes/{clienteId}");

        var resposta = await http.PostAsync($"/clientes/{clienteId}/reativar", null);
        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);

        var lista = await http.GetFromJsonAsync<List<ClienteNaLista>>("/clientes", Json);
        Assert.True(Assert.Single(lista!).Ativo);

        Assert.Equal(Anotacao, await AnotacaoNoBanco(conta.TenantId, clienteId));
    }

    [Fact]
    public async Task Reativar_duas_vezes_nao_e_erro()
    {
        var conta = await Contas.Criar(banco, _aplicacao);
        var clienteId = await Vincular(conta.TenantId);
        var http = await Contas.Entrar(_aplicacao, conta);

        await http.DeleteAsync($"/clientes/{clienteId}");

        /*
         * O segundo clique de quem não viu o primeiro chegar. Recusar o estado
         * que já vale faria a tela mostrar erro onde não houve nenhum.
         */
        await http.PostAsync($"/clientes/{clienteId}/reativar", null);
        var repetida = await http.PostAsync($"/clientes/{clienteId}/reativar", null);

        Assert.Equal(HttpStatusCode.NoContent, repetida.StatusCode);
    }

    [Fact]
    public async Task Cliente_de_outro_tenant_nao_pode_ser_desligado()
    {
        var contaA = await Contas.Criar(banco, _aplicacao);
        var contaB = await Contas.Criar(banco, _aplicacao);

        var doA = await Vincular(contaA.TenantId);
        var httpB = await Contas.Entrar(_aplicacao, contaB);

        /*
         * 404 e não 403: para o tenant B o cliente do A não existe. A política
         * de RLS filtra antes da consulta chegar ao registro, então nem o
         * endpoint sabe que houve um alvo.
         */
        var resposta = await httpB.DeleteAsync($"/clientes/{doA}");
        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);

        Assert.Equal(Anotacao, await AnotacaoNoBanco(contaA.TenantId, doA));
    }

    /* ------------------------------------------------------------- apoio */

    /// <summary>
    /// Uma pessoa e o vínculo de cliente, gravados direto. Passar pelo HTTP
    /// exigiria um documento válido novo a cada teste, e o que está em prova
    /// aqui não é a validação de documento.
    /// </summary>
    private async Task<Guid> Vincular(Guid tenantId)
    {
        var pessoaId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();

        await using var contexto = banco.Criar(tenantId);

        contexto.Pessoas.Add(new Pessoa
        {
            Id = pessoaId,
            TenantId = tenantId,
            Tipo = TipoPessoa.Juridica,
            Nome = "Padaria do Bairro Ltda",
            Documento = "11222333000181",
        });

        contexto.Clientes.Add(new Cliente
        {
            Id = clienteId,
            TenantId = tenantId,
            PessoaId = pessoaId,
            Codigo = "C-0001",
            Responsavel = "Shoiti",
            Observacoes = Anotacao,
        });

        await contexto.SaveChangesAsync();
        return clienteId;
    }

    private async Task<string?> AnotacaoNoBanco(Guid tenantId, Guid clienteId)
    {
        await using var contexto = banco.Criar(tenantId);

        return await contexto.Clientes.AsNoTracking()
            .Where(cliente => cliente.Id == clienteId)
            .Select(cliente => cliente.Observacoes)
            .FirstOrDefaultAsync();
    }
}
