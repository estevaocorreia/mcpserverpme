# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copia csproj e restaura
COPY Mcpserver/*.csproj ./Mcpserver/
RUN dotnet restore ./Mcpserver/Mcpserver.csproj

# Copia o restante e publica
COPY . .
RUN dotnet publish ./Mcpserver/Mcpserver.csproj -c Release -o /app/publish --no-restore

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Dependências nativas para LDAP
RUN apt-get update && apt-get install -y \
    libldap-2.5-0 \
    libsasl2-2 \
    libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

# Porta padrão interna
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

COPY --from=build /app/publish .

# Start em modo HTTP
ENTRYPOINT ["/app/Mcpserver", "--http"]
