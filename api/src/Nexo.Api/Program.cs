using System.IdentityModel.Tokens.Jwt;
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
using Nexo.Api.Servicos;

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

var conexao = ConexaoDoBanco.Resolver(construtor.Configuration);

construtor.Services.AddDbContext<NexoDbContext>((provedor, opcoes) => opcoes
    .UseNpgsql(conexao)
    .UseSnakeCaseNamingConvention()
    .AddInterceptors(provedor.GetRequiredService<InterceptorDeTenant>()));

/* ------------------------------------------------- serviços de fora */

/*
 * As duas saídas do sistema para fora: CEP e CNPJ.
 *
 * Nenhuma das duas sustenta nada. São atalhos de digitação, e o cadastro
 * funciona igual com as duas fora do ar — por isso o tempo de espera é curto.
 * Não é economia: se o atalho demorar mais do que digitar, deixou de ser
 * atalho.
 *
 * Os endereços saem da configuração para poderem ser trocados sem recompilar:
 * por um espelho, e por um destino inalcançável, que é o único jeito de
 * exercitar o caminho de "serviço fora do ar".
 */
construtor.Services.AddHttpClient<IConsultaDeCnpj, ConsultaDeCnpjBrasilApi>(cliente =>
{
    cliente.BaseAddress = new Uri(
        construtor.Configuration["Servicos:Cnpj:Endereco"] ?? "https://brasilapi.com.br/");

    /* Mais folga que o CEP: consulta ao cadastro da Receita é mais lenta, e
       quem digita CNPJ está esperando o formulário inteiro se preencher. */
    cliente.Timeout = TimeSpan.FromSeconds(6);
});

construtor.Services.AddHttpClient<IConsultaDeCep, ConsultaDeCepViaCep>(cliente =>
{
    cliente.BaseAddress = new Uri(
        construtor.Configuration["Servicos:Cep:Endereco"] ?? "https://viacep.com.br/");

    cliente.Timeout = TimeSpan.FromSeconds(4);
});

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

        /*
         * O id do usuário viaja na claim `sub`, que é o nome padrão do JWT, e
         * o handler não remapeia claims (MapInboundClaims = false). O Identity,
         * porém, procura o id em `NameIdentifier` — e sem esta linha o
         * `GetUserAsync` não encontra ninguém numa requisição perfeitamente
         * autenticada, respondendo 401 onde deveria trabalhar.
         */
        opcoes.ClaimsIdentity.UserIdClaimType = JwtRegisteredClaimNames.Sub;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<NexoDbContext>()
    .AddErrorDescriber<DescritorDeErros>()
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

            /*
             * Assinatura válida não basta: o carimbo precisa bater com o banco.
             *
             * Sem esta conferência, trocar a senha não derruba nada. O token é
             * autocontido — não há lista de sessões para apagar —, então uma
             * sessão aberta em outro navegador seguiria valendo até expirar. No
             * dia em que a troca for por suspeita de vazamento, é exatamente
             * essa sessão que precisa cair, e é a única que importa.
             *
             * Custa uma consulta por requisição, e é uma consulta por chave
             * primária numa tabela sem RLS. Foi decisão consciente: conferir de
             * vez em quando, como o cookie do Identity faz por padrão, deixa uma
             * janela em que a senha já mudou e a sessão antiga ainda abre porta.
             */
            OnTokenValidated = async contexto =>
            {
                var carimbo = contexto.Principal?.FindFirst(Sessao.ClaimCarimbo)?.Value;
                var sujeito = contexto.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

                if (string.IsNullOrEmpty(carimbo) || !Guid.TryParse(sujeito, out var usuarioId))
                {
                    /* Token nosso, assinado por nós, sem o que precisamos: é de
                       antes desta conferência existir. Cai como qualquer outro. */
                    contexto.Fail("Token sem carimbo de segurança.");
                    return;
                }

                /*
                 * Escopo próprio para não encostar no DbContext da requisição,
                 * que ainda nem começou a servir o endpoint.
                 */
                using var escopo = contexto.HttpContext.RequestServices
                    .GetRequiredService<IServiceScopeFactory>().CreateScope();

                var banco = escopo.ServiceProvider.GetRequiredService<NexoDbContext>();

                var atual = await banco.Users.AsNoTracking()
                    .Where(usuario => usuario.Id == usuarioId)
                    .Select(usuario => usuario.SecurityStamp)
                    .FirstOrDefaultAsync(contexto.HttpContext.RequestAborted);

                if (atual is null || atual != carimbo)
                    contexto.Fail("Sessão encerrada: a senha mudou desde que este acesso começou.");
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
/*
 * Corpo ilegível vira exceção, para o middleware lá embaixo poder respondê-la
 * no formato da casa.
 *
 * Sem isto a API minimalista trata o caso sozinha e devolve 400 com corpo
 * vazio — e, pior, faz isso só fora de Development, onde o padrão já é lançar.
 * Ou seja: o comportamento mudava entre a máquina de quem programa e a
 * produção, que é onde ninguém quer descobrir diferença.
 */
construtor.Services.Configure<RouteHandlerOptions>(opcoes => opcoes.ThrowOnBadRequest = true);

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
 * Extrair o documento OpenAPI monta e **executa** a aplicação até aqui, só para
 * ler a tabela de rotas. Sem esta saída antecipada, gerar o contrato passaria a
 * exigir um banco no ar — e falharia em qualquer clone limpo, em qualquer CI.
 *
 * O que se pula são efeitos de subida, não proteção: nada abaixo disto valida
 * requisição nem libera acesso.
 */
var extraindoContrato = Environment.GetEnvironmentVariable("NEXO_EXTRAINDO_CONTRATO") == "1";

if (!extraindoContrato)
{
    /*
     * Migrar acontece em todo ambiente, inclusive produção — é o que faz uma
     * implantação encontrar as tabelas que ela espera. Ver MigracaoNaSubida.
     */
    await MigracaoNaSubida.AplicarAsync(app.Services);

    /*
     * Conferir o papel depois de migrar: as políticas precisam existir para
     * a pergunta fazer sentido. Ver ConferenciaDoPapel — é o único erro desta
     * implantação que não tem sintoma nenhum.
     */
    await ConferenciaDoPapel.VerificarAsync(app.Services, app.Environment);

/*
 * Provisionamento do primeiro tenant.
 *
 * Roda em qualquer ambiente, mas só faz alguma coisa quando o banco está vazio
 * E alguém disse o que criar. Em produção sem configuração, nada acontece — de
 * propósito. Ver ProvisionamentoInicial.
 */
    if (ProvisionamentoInicial.Ler(construtor.Configuration, app.Environment) is { } inicial)
        await ProvisionamentoInicial.ExecutarAsync(app.Services, inicial);

    /*
     * A saída para quem perdeu a senha.
     *
     * Vem depois do provisionamento de propósito: num banco vazio, quem cria o
     * usuário é ele, e só faz sentido redefinir uma senha que já existe.
     * Ver RecuperacaoDeSenha.
     */
    if (RecuperacaoDeSenha.Ler(construtor.Configuration) is { } recuperacao)
        await RecuperacaoDeSenha.ExecutarAsync(app.Services, recuperacao);
}

/*
 * Corpo malformado devolve o mesmo formato de erro que todo o resto.
 *
 * Sem isto o ASP.NET responde 400 com um texto seco e sem estrutura, e um
 * cliente que sabe ler `problemas` recebe algo que não sabe ler — justamente
 * quando já está confuso. Só acontece com cliente defeituoso, porque o nosso é
 * gerado do contrato, e é exatamente por isso que a mensagem precisa ser clara:
 * quem topa com ela está depurando.
 *
 * Vem antes da autenticação de propósito. A leitura do corpo acontece na
 * ligação dos parâmetros, depois disto na fila, e o middleware precisa estar
 * por fora para pegar o estouro.
 */
app.Use(async (contexto, seguir) =>
{
    try
    {
        await seguir(contexto);
    }
    catch (BadHttpRequestException erro) when (!contexto.Response.HasStarted)
    {
        contexto.Response.StatusCode = StatusCodes.Status400BadRequest;
        contexto.Response.ContentType = "application/json; charset=utf-8";

        await contexto.Response.WriteAsJsonAsync(new RespostaComProblemas([new Problema(
            "corpo",
            "Requisição malformada",
            "O corpo da requisição não pôde ser lido.",
            "Confira se o JSON está bem formado e se os tipos batem com o contrato em /swagger. " +
            "Detalhe: " + erro.Message)]));
    }
});

app.UseAuthentication();
app.UseAuthorization();

app.MapSaude();
app.MapAcesso();
app.MapEmpresas();
app.MapPessoas();
app.MapClientes();
app.MapContratos();
app.MapRecebiveis();
app.MapConsultas();

app.Run();

/*
 * Público só para o projeto de testes conseguir subir a aplicação inteira com
 * WebApplicationFactory.
 */
public partial class Program;
