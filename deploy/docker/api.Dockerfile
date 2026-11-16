# Task 052 — the API image. Build from the repository root:
#   docker build -f deploy/docker/api.Dockerfile --build-arg VERSION=1.2.3 -t ash-api:1.2.3 .
# Runtime: the full noble ASP.NET image (ICU + tzdata: sq-AL formats and Europe/Tirane — the app refuses to start
# without them, RuntimeSelfCheck), non-root (the image's "app" user), read-only root filesystem (mount /tmp as a
# tmpfs: exports are written to a temporary file), no secrets (configuration from the environment / mounted secrets).
# The API never runs migrations: that is the migrator image (deploy/docker/migrator.Dockerfile).
# Base images pinned by tag AND digest (refresh: deploy/README.md, "Updating the base images").

FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
WORKDIR /src
COPY src/backend/Directory.Packages.props src/backend/Directory.Build.props* src/backend/
COPY src/backend/BuildingBlocks src/backend/BuildingBlocks
COPY src/backend/Modules src/backend/Modules
COPY src/backend/Host src/backend/Host
COPY src/backend/Database src/backend/Database
ARG VERSION=0.0.0-local
RUN dotnet restore src/backend/Host/Ash.Api/Ash.Api.csproj
RUN dotnet publish src/backend/Host/Ash.Api/Ash.Api.csproj -c Release -o /app --no-restore \
    -p:Version=${VERSION} -p:InformationalVersion=${VERSION} -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4 AS runtime
ARG VERSION=0.0.0-local
LABEL org.opencontainers.image.title="ash-api" org.opencontainers.image.version="${VERSION}"
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_ENVIRONMENT=Production \
    Ash__Version=${VERSION}
WORKDIR /app
COPY --from=build --chown=root:root /app .
# the image's non-root user (UID 1654); the files stay owned by root and read-only for it
USER $APP_UID
EXPOSE 8080
# liveness without curl/wget in the image: bash's /dev/tcp, the process-only endpoint
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health/live HTTP/1.0\\r\\nHost: localhost\\r\\n\\r\\n' >&3 && head -1 <&3 | grep -q ' 200 '"]
ENTRYPOINT ["dotnet", "Ash.Api.dll"]
