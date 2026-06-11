# ─────────────────────────────────────────────────────────────
# AniMatch · Multi-stage build (ARM64-friendly)
# ─────────────────────────────────────────────────────────────

# Etapa 1 — restore + publish
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# El csproj primero, para aprovechar la caché de capas de Docker
COPY AniMatch.csproj ./
RUN dotnet restore AniMatch.csproj

COPY . .
RUN dotnet publish AniMatch.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Etapa 2 — runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app

# Usuario sin privilegios
RUN groupadd -r animatch && useradd -r -g animatch -d /app animatch \
 && chown -R animatch:animatch /app
USER animatch

COPY --from=build --chown=animatch:animatch /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

ENTRYPOINT ["dotnet", "AniMatch.dll"]
