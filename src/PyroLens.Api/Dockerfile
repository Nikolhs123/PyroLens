# Build context is the repository root (see docker-compose.yml).

# 1. The map (React + TypeScript), built to static files.
FROM node:22.12.0-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

# 2. The API.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/ src/
RUN dotnet publish src/PyroLens.Api/PyroLens.Api.csproj -c Release -o /app

# 3. Runtime: the API serves the map from wwwroot on the same origin.
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
COPY --from=web /web/dist ./wwwroot
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "PyroLens.Api.dll"]
