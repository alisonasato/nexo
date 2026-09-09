using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Nexo.Api.Dados;

namespace Nexo.Api.Testes;

/// <summary>
/// O provisionamento do primeiro tenant.
///
/// A propriedade que mais importa aqui é negativa: <b>em produção, sem alguém
/// dizer o que criar, não nasce nada.</b> Um tenant de demonstração com senha
/// conhecida aparecendo sozinho num sistema com dinheiro dentro seria pior do
/// que não ter provisionamento nenhum.
/// </summary>
[Collection(nameof(ColecaoDoBanco))]
public class Provisionamento(BancoDeTestes banco) : IDisposable
{
    private readonly AplicacaoDeTestes _aplicacao = new(banco.Conexao);

    public void Dispose() => _aplicacao.Dispose();

    private static IConfiguration Configuracao(params (string Chave, string Valor)[] valores) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(valores.ToDictionary(v => v.Chave, v => (string?)v.Valor))
            .Build();

    private sealed class Ambiente(string nome) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nome;
        public string ApplicationName { get; set; } = "Nexo.Api";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public void Em_producao_sem_configuracao_nao_provisiona_nada()
    {
        var decisao = ProvisionamentoInicial.Ler(Configuracao(), new Ambiente("Production"));

        Assert.Null(decisao);
    }

    [Fact]
    public void Em_producao_com_email_mas_sem_senha_tambem_nao_provisiona()
    {
        // Configuração pela metade não vira credencial inventada.
        var decisao = ProvisionamentoInicial.Ler(
            Configuracao(("Provisionamento:Email", "alguem@escritorio.com.br")),
            new Ambiente("Production"));

        Assert.Null(decisao);
    }

    [Fact]
    public void Em_desenvolvimento_sem_configuracao_usa_a_demonstracao()
    {
        var decisao = ProvisionamentoInicial.Ler(Configuracao(), new Ambiente("Development"));

        Assert.Equal(ProvisionamentoInicial.Demonstracao, decisao);
    }

    [Fact]
    public void A_configuracao_vence_ate_em_desenvolvimento()
    {
        var decisao = ProvisionamentoInicial.Ler(
            Configuracao(
                ("Provisionamento:TenantNome", "Contabilidade Aurora"),
                ("Provisionamento:Email", "ana@aurora.com.br"),
                ("Provisionamento:Senha", "SenhaForte@2026"),
                ("Provisionamento:EmpresaRazaoSocial", "Aurora Contabilidade Ltda"),
                ("Provisionamento:EmpresaCnpj", "11.222.333/0001-81")),
            new Ambiente("Development"));

        Assert.NotNull(decisao);
        Assert.Equal("Contabilidade Aurora", decisao.TenantNome);
        Assert.Equal("ana@aurora.com.br", decisao.Email);

        // O CNPJ é guardado só com dígitos, como em todo o resto do sistema.
        Assert.Equal("11222333000181", decisao.EmpresaCnpj);
    }

    [Fact]
    public async Task Com_o_banco_ja_povoado_nao_faz_nada()
    {
        // A suíte inteira já criou tenants neste banco, que é justamente o caso.
        await Contas.Criar(banco, _aplicacao);

        var criou = await ProvisionamentoInicial.ExecutarAsync(
            _aplicacao.Services,
            new DadosDoProvisionamento(
                "Não deveria nascer", "intruso@exemplo.com.br", "SenhaForte@2026",
                "Não deveria nascer Ltda", "11222333000181"));

        Assert.False(criou);
    }
}
