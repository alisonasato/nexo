# Imagem da API.
#
# Existe porque o Railpack, o construtor automático da Railway, não suporta
# .NET — a documentação deles manda usar Dockerfile para ASP.NET Core. Para
# Node continua valendo a detecção automática, e por isso o front não tem um.
#
# O `Root Directory` do serviço aponta para esta pasta, então o contexto de
# build é ela: os caminhos abaixo são relativos ao projeto, não à raiz do
# repositório.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS construcao
WORKDIR /app

# O .csproj sozinho primeiro: enquanto as dependências não mudarem, o Docker
# reaproveita a camada do restore e o build fica muito mais rápido.
COPY *.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish --no-restore -c Release -o publicado

# A imagem final não leva o SDK — só o runtime. São centenas de megabytes de
# compilador que não servem para nada em produção.
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=construcao /app/publicado ./

# Sem ASPNETCORE_URLS aqui, de propósito: a aplicação lê PORT e liga em
# [::]:$PORT, que atende IPv4 e IPv6 — a rede privada da Railway é IPv6.
# Definir ASPNETCORE_URLS sobrescreveria isso com um endereço pior.

ENTRYPOINT ["dotnet", "Nexo.Api.dll"]
