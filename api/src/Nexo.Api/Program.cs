using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Nexo.Api;
using Nexo.Api.Autenticacao;
using Nexo.Api.OpenApi;
using Nexo.Api.Dados;
using Nexo.Api.Dominio;
using Nexo.Api.Endpoints;

var construtor = WebApplication.CreateBuilder(args);

/*
 * A porta vem do ambiente quando ele manda uma.
 *
 * O PaaS (decisão Q31) escolhe a porta e a injeta em PORT; a aplicação precisa
 * escutar exatamente nela, ou o roteador não a encontra e toda requisição
 * responde que a aplicação não respondeu.
 *
 * O endereço é [::] e não 0.0.0.0. Ligar só em IPv4 funciona para o tráfego
 * público, mas a rede privada entre serviços da Railway é IPv6 — e ligar só
 * nela deixaria de fora ambientes legados. [::] atende os dois, que é o que a
 * documentação deles recomenda.
 *
 * Sem PORT no ambiente, nada muda: vale o --urls ou o launchSettings de sempre.
 */
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } portaDoAmbiente)
    construtor.WebHost.UseUrls($"http://[::]:{portaDoAmbiente}");

/* ------------------------------------------------------------------ CORS */

/*
 * CORS não está mais no caminho da aplicação.
 *
 * O front serve a API no próprio domínio, por reescrita de rota (ver
 * web/next.config.ts): o navegador chama /api no host do front e nunca fala
 * com esta porta. Não há requisição entre origens para liberar — em
 * desenvolvimento nem em produção.
 *
 * O que sobrou serve a um caso só: chamar a API direto de uma ferramenta ou de
 * uma página aberta em outra origem, durante o desenvolvimento. Por isso
 * continua registrado apenas nos ambientes locais.
 *
 * AllowCredentials segue obrigatório se essa chamada direta levar o cookie, e
 * ele proíbe origem coringa — daí a origem ser explícita, e não AllowAnyOrigin.
 */
const string PoliticaDeDesenvolvimento = "desenvolvimento";

var origensPermitidas = construtor.Configuration.GetSection("Cors:Origens").Get<string[]>()
    ?? ["http://localhost:3000"];

construtor.Services.AddCors(opcoes => opcoes.AddPolicy(PoliticaDeDesenvolvimento, politica => politica
    .WithOrigins(origensPermitidas)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

/* ----------------------------------------------------------------- dados */

construtor.Services.AddHttpContextAccessor();
construtor.Services.AddScoped<IContextoDeTenant, ContextoDeTenantHttp>();
construtor.Services.AddScoped<InterceptorDeTenant>();

construtor.Services.AddDbContext<NexoDbContext>((provedor, opcoes) => opcoes
    .UseNpgsql(construtor.Configuration.GetConnectionString("Nexo"))
    .UseSnakeCaseNamingConvention()
    .AddInterceptors(provedor.GetRequiredService<InterceptorDeTenant>()));

/* --------------------------------------------------------- autenticação */

construtor.Services.Configure<OpcoesDeToken>(construtor.Configuration.GetSection(OpcoesDeToken.Secao));
construtor.Services.AddScoped<GeradorDeToken>();

var opcoesDeToken = construtor.Configuration.GetSection(OpcoesDeToken.Secao).Get<OpcoesDeToken>()
    ?? new OpcoesDeToken();

/*
 * Sem chave, a aplicação não sobe. Um valor padrão aqui seria um segredo
 * versionado, e todo mundo que clonasse o repositório saberia assinar tokens
 * válidos da produção alheia.
 */
if (string.IsNullOrWhiteSpace(opcoesDeToken.Chave) || Encoding.UTF8.GetByteCount(opcoesDeToken.Chave) < 32)
{
    throw new InvalidOperationException(
        "Configure Jwt:Chave com pelo menos 32 bytes. Em desenvolvimento ela está em " +
        "appsettings.Development.json; fora dele, use `dotnet user-secrets` ou variável de ambiente.");
}

construtor.Services.AddIdentityCore<Usuario>(opcoes =>
    {
        opcoes.User.RequireUniqueEmail = true;
        opcoes.Password.RequiredLength = 10;
        opcoes.Lockout.MaxFailedAccessAttempts = 5;
        opcoes.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<NexoDbContext>()
    .AddSignInManager();

construtor.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opcoes =>
    {
        /*
         * O token não vem no cabeçalho Authorization: vem do cookie httpOnly
         * (decisão Q29). É só de onde ele é lido que muda — a validação é a
         * mesma.
         */
        opcoes.Events = new JwtBearerEvents
        {
            OnMessageReceived = contexto =>
            {
                if (contexto.Request.Cookies.TryGetValue(Sessao.Cookie, out var token))
                    contexto.Token = token;
                return Task.CompletedTask;
            },
        };

        /*
         * Sem isto o handler renomeia as claims para URIs longas do WS-Federation
         * e `tenant_id` deixa de ser encontrável pelo nome com que foi escrita.
         */
        opcoes.MapInboundClaims = false;

        opcoes.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = opcoesDeToken.Emissor,
            ValidateAudience = true,
            ValidAudience = opcoesDeToken.Publico,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opcoesDeToken.Chave)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

construtor.Services.AddAuthorization(opcoes =>
{
    /*
     * Fechado por padrão. Um endpoint novo nasce exigindo sessão, e abrir é um
     * ato explícito com AllowAnonymous — o inverso disso é como se esquece de
     * proteger uma rota.
     */
    opcoes.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

/*
 * Enum vai no JSON como texto, não como número.
 *
 * "Fisica" se lê; 1 não. O contrato é lido por gente e por gerador de código,
 * e um inteiro nu obriga os dois a consultar uma tabela que não está no
 * documento. Também protege contra reordenar o enum em C# e mudar o
 * significado do dado já gravado nos clientes.
 */
construtor.Services.ConfigureHttpJsonOptions(opcoes =>
    opcoes.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

/* --------------------------------------------------------------- contrato */

construtor.Services.AddEndpointsApiExplorer();
construtor.Services.AddSwaggerGen(opcoes =>
{
    opcoes.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Nexo",
        Version = "v1",
        Description = "API do Nexo. Este documento é o contrato: o cliente TypeScript do front é gerado a partir dele, nunca escrito à mão.",
    });

    opcoes.SupportNonNullableReferenceTypes();
    opcoes.UseAllOfToExtendReferenceSchemas();
    opcoes.SchemaFilter<PropriedadesNaoAnulaveisSaoObrigatorias>();
    opcoes.SchemaFilter<EnumsComoTexto>();
});

var app = construtor.Build();

/*
 * "Local" é desenvolvimento e o ambiente dos testes de integração. Os dois
 * falam http, e redirecionar para https ali responde 307 antes de o CORS ser
 * avaliado: o front morre com um erro de origem que não tem nada a ver com a
 * causa, e o cliente de teste segue um redirecionamento que não existe.
 */
var ambienteLocal = app.Environment.IsDevelopment() || app.Environment.IsEnvironment(Ambientes.Testes);

if (ambienteLocal)
{
    app.UseCors(PoliticaDeDesenvolvimento);
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    /*
     * Atrás do proxy do PaaS (decisão Q31).
     *
     * Quem termina o TLS é o proxy; até a aplicação, a requisição chega em
     * http. Sem ler os cabeçalhos encaminhados, ela acredita que o mundo
     * inteiro é http — e o UseHttpsRedirection abaixo não achava porta https
     * para onde redirecionar, avisava "Failed to determine the https port" no
     * log e não fazia absolutamente nada. Uma proteção que só parecia existir.
     *
     * Com os cabeçalhos lidos, o esquema original volta a ser conhecido — que
     * é o que faz a aplicação gerar URL certa e saber que a conexão é segura.
     *
     * Limpar KnownNetworks e KnownProxies significa confiar no salto
     * imediatamente à frente. Vale porque no PaaS só o proxy alcança a
     * aplicação. Se um dia ela ficar exposta direto, isto vira um buraco: um
     * cliente poderia afirmar "vim por https" sem ter vindo.
     */
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor,
        KnownNetworks = { },
        KnownProxies = { },
    });

    /*
     * Diz ao navegador para nunca mais tentar http neste domínio.
     * (Em localhost o middleware não emite o cabeçalho, de propósito — foi o
     * que confundiu a primeira verificação daqui.)
     */
    app.UseHsts();

    /*
     * Não há UseHttpsRedirection, e a ausência é deliberada.
     *
     * Sem uma porta https configurada ele não tem para onde redirecionar:
     * registra "Failed to determine the https port" e deixa a requisição
     * passar. Redirecionar http para https é trabalho do proxy que termina o
     * TLS (decisão Q31), e ele já faz. Manter aqui um middleware que só avisa
     * e não age é guardar uma proteção de mentira — o mesmo defeito que este
     * bloco veio corrigir.
     */
}

/*
 * Migrar acontece em todo ambiente, inclusive produção — é o que faz uma
 * implantação encontrar as tabelas que ela espera. Ver MigracaoNaSubida.
 */
await MigracaoNaSubida.AplicarAsync(app.Services);

/*
 * Provisionamento do primeiro tenant.
 *
 * Roda em qualquer ambiente, mas só faz alguma coisa quando o banco está vazio
 * E alguém disse o que criar. Em produção sem configuração, nada acontece — de
 * propósito. Ver ProvisionamentoInicial.
 */
if (ProvisionamentoInicial.Ler(construtor.Configuration, app.Environment) is { } inicial)
    await ProvisionamentoInicial.ExecutarAsync(app.Services, inicial);

app.UseAuthentication();
app.UseAuthorization();

app.MapSaude();
app.MapAcesso();
app.MapEmpresas();
app.MapPessoas();
app.MapClientes();
app.MapContratos();
app.MapRecebiveis();

app.Run();

/*
 * Público só para o projeto de testes conseguir subir a aplicação inteira com
 * WebApplicationFactory.
 */
public partial class Program;
