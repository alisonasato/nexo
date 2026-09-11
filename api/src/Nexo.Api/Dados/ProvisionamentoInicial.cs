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
    /// O que há de errado com a senha, segundo as regras do Identity. Lista
    /// vazia quer dizer que serve.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ConferirSenha(
        UserManager<Usuario> usuarios,
        string senha,
        string nome = "Provisionamento")
    {
        var problemas = new List<string>();
        var candidato = new Usuario { Nome = nome };

        foreach (var validador in usuarios.PasswordValidators)
        {
            var conferencia = await validador.ValidateAsync(usuarios, candidato, senha);
            if (!conferencia.Succeeded)
                problemas.AddRange(conferencia.Errors.Select(erro => erro.Description));
        }

        return problemas;
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

        var usuarios = provedor.GetRequiredService<UserManager<Usuario>>();

        /*
         * A senha é conferida ANTES de escrever qualquer coisa.
         *
         * Sem isto, senha fraca criava o tenant, criava a empresa, e só então
         * falhava no usuário — deixando um banco que parece povoado, onde
         * ninguém entra, e onde a trava do "banco vazio" impede a segunda
         * tentativa. Corrigir exigia apagar linhas à mão no banco de produção.
         *
         * Aconteceu de verdade numa implantação. A ordem certa é conferir
         * primeiro e escrever depois, e é a mesma lição de RecuperacaoDeSenha.
         */
        var problemas = await ConferirSenha(usuarios, dados.Senha, dados.TenantNome);

        if (problemas.Count > 0)
        {
            throw new InvalidOperationException(
                $"A senha em {Secao}__Senha não serve: " + string.Join(" ", problemas) +
                " Nada foi criado no banco — corrija a variável e implante de novo.");
        }

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
             * Desfaz o que escreveu antes de estourar.
             *
             * A senha já foi conferida acima, então chegar aqui é outra coisa —
             * e-mail já usado, por exemplo. Seja o que for, o banco não pode
             * ficar pela metade: um tenant e uma empresa sem ninguém que
             * consiga entrar parecem um banco povoado, e a trava do "banco
             * vazio" impediria a segunda tentativa de consertar.
             */
            await using (var comoTenant = new NexoDbContext(opcoes))
            {
                await comoTenant.Empresas.Where(e => e.Id == empresaId).ExecuteDeleteAsync(cancelamento);
            }

            await banco.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync(cancelamento);

            throw new InvalidOperationException(
                "O usuário inicial falhou: " +
                string.Join("; ", resultado.Errors.Select(erro => erro.Description)) +
                " O tenant e a empresa criados foram apagados, então o banco voltou ao estado " +
                "anterior — corrija a configuração e implante de novo.");
        }

        registro.LogInformation(
            "Banco vazio: criado o tenant “{Tenant}” com o usuário {Email}. " +
            "Remova as variáveis de {Secao} depois de entrar pela primeira vez.",
            dados.TenantNome, dados.Email, Secao);

        return true;
    }
}
