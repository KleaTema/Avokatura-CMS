# Task 052 — the migrator: a run-once container that applies the DbUp scripts (embedded in Ash.Database) with the
# MIGRATOR account (ConnectionStrings__Migrator, SslMode=Required), prints what it ran, and exits 0 / non-0.
# The API never migrates. Build from the repository root:
#   docker build -f deploy/docker/migrator.Dockerfile --build-arg VERSION=1.2.3 -t ash-migrator:1.2.3 .
#   docker run --rm -e ConnectionStrings__Migrator="Server=…;SslMode=Required;…" ash-migrator:1.2.3
# (no --local: the LocalScripts — dev users and test data — never run outside development)

FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
WORKDIR /src
COPY src/backend/Directory.Packages.props src/backend/Directory.Build.props* src/backend/
COPY src/backend/Database src/backend/Database
ARG VERSION=0.0.0-local
RUN dotnet restore src/backend/Database/Ash.Database/Ash.Database.csproj
RUN dotnet publish src/backend/Database/Ash.Database/Ash.Database.csproj -c Release -o /app --no-restore \
    -p:Version=${VERSION} -p:InformationalVersion=${VERSION} -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0.12-noble@sha256:b89586dc17781f25531909993658aa8161205ae38b8cec8847df4a8221a403d5 AS runtime
ARG VERSION=0.0.0-local
LABEL org.opencontainers.image.title="ash-migrator" org.opencontainers.image.version="${VERSION}"
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false DOTNET_EnableDiagnostics=0
WORKDIR /app
COPY --from=build --chown=root:root /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Ash.Database.dll"]
