# syntax=docker/dockerfile:1

# --- Web app -----------------------------------------------------------------
FROM node:22-alpine AS web
WORKDIR /src/web/hunters-web
COPY web/hunters-web/package.json web/hunters-web/package-lock.json ./
RUN npm ci
COPY design/ /src/design/
COPY web/hunters-web/ ./
RUN npm run build

# --- Server ------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Hunters.Server/Hunters.Server.csproj src/Hunters.Server/
COPY src/Hunters.Contracts/Hunters.Contracts.csproj src/Hunters.Contracts/
COPY src/Hunters.Domain/Hunters.Domain.csproj src/Hunters.Domain/
COPY src/Hunters.ServiceDefaults/Hunters.ServiceDefaults.csproj src/Hunters.ServiceDefaults/
COPY src/Hunters.Intel/Hunters.Intel.csproj src/Hunters.Intel/
COPY src/Hunters.Intel.Data/Hunters.Intel.Data.csproj src/Hunters.Intel.Data/
RUN dotnet restore src/Hunters.Server/Hunters.Server.csproj && dotnet restore src/Hunters.Intel/Hunters.Intel.csproj
COPY src/ src/
RUN dotnet publish src/Hunters.Server/Hunters.Server.csproj -c Release -o /app/publish --no-restore
RUN dotnet publish src/Hunters.Intel/Hunters.Intel.csproj -c Release -o /app/publish-intel --no-restore

# --- Intel service (build with --target intel) ---------------------------------
# Before the server stage on purpose: a plain `docker build` builds the last stage, the server.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS intel
WORKDIR /app
COPY --from=build /app/publish-intel ./
USER $APP_UID
ENTRYPOINT ["dotnet", "NewEdenSoft.Hunters.Intel.dll"]

# --- Server (the default target) ----------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Volume mount points, owned by the non-root app user so named volumes inherit the ownership.
RUN mkdir -p /app/keys /app/sde && chown "$APP_UID" /app/keys /app/sde

COPY --from=build /app/publish ./
COPY --from=web /src/web/hunters-web/dist ./wwwroot

ENV Hunters__DataProtection__KeysPath=/app/keys
ENV Hunters__Sde__CachePath=/app/sde
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "NewEdenSoft.Hunters.Server.dll"]
