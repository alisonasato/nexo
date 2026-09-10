using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nexo.Api.Dominio;

namespace Nexo.Api.Dados;

/// <param name="TenantNome">Nome do escritório.</param>
/// <param name="Email">E-mail de quem vai entrar pela primeira vez.</param>
/// <param name="Senha">Precisa passar pelas regras do Identity.</param>
/// <param name="EmpresaRazaoSocial">Razão social do próprio escritório.</param>
/// <param name="EmpresaCnpj">Só os dígitos.</param>
public record DadosDoProvisionamento(
    string TenantNome,
    string Email,
    string Senha,
    string EmpresaRazaoSocial,
    string EmpresaCnpj);

/// <summary>
/// Cria o primeiro tenant, a empresa e o usuário — o mínimo para alguém
/// conseguir entrar num banco recém-criado.
///
/// <para>
/// Existe porque implantar deixava o sistema inutilizável: o banco subia vazio,
/// a semeadura só rodava em desenvolvimento, e as saídas eram SQL na mão ou
/// subir uma vez como <c>Development</c> só para semear — a segunda troca junto
/// a chave de assinatura e o CORS, o que é jeito de consertar uma coisa
/// quebrando três.
/// </para>
/// <para>
/// <b>Duas travas, e a segunda é a que importa.</b> Só age com o banco vazio, e
/// só age quando alguém <b>disse</b> o que criar. Em produção sem configuração
/// não acontece nada: um tenant de demonstração com senha conhecida nascendo
/// sozinho num sistema com dinheiro dentro seria pior do que não ter
/// provisionamento nenhum. O padrão de demonstração vale só em desenvolvimento.
/// </para>
/// </summary>
public static class ProvisionamentoInicial
{
    public const string Secao = "Provisionamento";

    /// <summary>As credenciais de desenvolvimento, e só delas.</summary>
    public static readonly DadosDoProvisionamento Demonstracao = new(
        TenantNome: "Escritório Demonstração",
        Email: "contato@escritoriodemo.com.br",
        Senha: "NexoDev@2026",
        EmpresaRazaoSocial: "Escritório Demonstração Contabilidade Ltda",
        EmpresaCnpj: "11222333000181");

    /// <summary>
    /// O que provisionar, ou <c>null</c> quando não se deve provisionar nada.
    /// </summary>
    public static DadosDoProvisionamento? Ler(IConfiguration configuracao, IHostEnvironment ambiente)
    {
        var secao = configuracao.GetSection(Secao);

        var email = secao["Email"];
        var senha = secao["Senha"];

        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(senha))
        {
            return new DadosDoProvisionamento(
                secao["TenantNome"] ?? "Escritório",
                email.Trim(),
                senha,
                secao["EmpresaRazaoSocial"] ?? secao["TenantNome"] ?? "Escritório",
                Documento.ApenasDigitos(secao["EmpresaCnpj"]));
        }

        /* Sem configuração: só desenvolvimento ganha um tenant de brinde. */
        return ambiente.IsDevelopment() ? Demonstracao : null;
    }

    /// <summary>
    /// Cria o tenant se ainda não houver nenhum. Devolve <c>true</c> quando
    /// criou, <c>false</c> quando encontrou o banco já povoado.
    /// </summary>
    public static async Task<bool> ExecutarAsync(
        IServiceProvider servicos,
        DadosDoProvisionamento dados,
        CancellationToken cancelamento = default)
    {
        using var escopo = servicos.CreateScope();
        var provedor = escopo.ServiceProvider;

        var banco = provedor.GetRequiredService<NexoDbContext>();
        var registro = provedor.GetRequiredService<ILoggerFactory>().CreateLogger("Provisionamento");

        if (await banco.Tenants.AnyAsync(cancelamento)) return false;

        var tenantId = Guid.NewGuid();
        banco.Tenants.Add(new Tenant { Id = tenantId, Nome = dados.TenantNome });
        await banco.SaveChangesAsync(cancelamento);

        /*
         * A empresa está sob RLS, e aqui não existe requisição HTTP — logo não
         * há claim, logo não há tenant. Um contexto com o tenant fixo é o
         * caminho honesto: em vez de abrir uma porta dos fundos no contexto que
         * a aplicação usa, declara-se de uma vez em nome de quem se escreve.
         */
        var configuracao = provedor.GetRequiredService<IConfiguration>();
        var opcoes = new DbContextOptionsBuilder<NexoDbContext>()
            .UseNpgsql(ConexaoDoBanco.Resolver(configuracao))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new InterceptorDeTenant(new ContextoDeTenantFixo(tenantId)))
            .Options;

        var empresaId = Guid.NewGuid();
        await using (var comoTenant = new NexoDbContext(opcoes))
        {
            comoTenant.Empresas.Add(new Empresa
            {
                Id = empresaId,
                TenantId = tenantId,
                RazaoSocial = dados.EmpresaRazaoSocial,
                Cnpj = dados.EmpresaCnpj,
            });
            await comoTenant.SaveChangesAsync(cancelamento);
        }

        var usuarios = provedor.GetRequiredService<UserManager<Usuario>>();
        var resultado = await usuarios.CreateAsync(new Usuario
        {
            Id = Guid.NewGuid(),
            UserName = dados.Email,
            Email = dados.Email,
            EmailConfirmed = true,
            Nome = dados.TenantNome,
            TenantId = tenantId,
            EmpresaPadraoId = empresaId,
        }, dados.Senha);

        if (!resultado.Succeeded)
        {
            /*
             * Estourar em vez de deixar meio feito. Um tenant e uma empresa sem
             * ninguém que consiga entrar é um banco que parece povoado e não
             * serve para nada — e a trava do "banco vazio" impediria a segunda
             * tentativa de consertar.
             */
            throw new InvalidOperationException(
                "Tenant criado, mas o usuário inicial falhou: " +
                string.Join("; ", resultado.Errors.Select(erro => erro.Description)) +
                " O banco ficou pela metade; apague o tenant criado antes de tentar de novo.");
        }

        registro.LogInformation(
            "Banco vazio: criado o tenant “{Tenant}” com o usuário {Email}. " +
            "Remova as variáveis de {Secao} depois de entrar pela primeira vez.",
            dados.TenantNome, dados.Email, Secao);

        return true;
    }
}
