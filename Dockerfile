# syntax=docker/dockerfile:1

# Stage 1: build the web UI (Vite → web/dist)
FROM node:26-bookworm-slim AS web
WORKDIR /build/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# Stage 2: build the server (restore → publish)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server
WORKDIR /build
COPY OpenHome.slnx ./
COPY src/OpenHome.Core/OpenHome.Core.csproj src/OpenHome.Core/
COPY src/OpenHome.Formats/OpenHome.Formats.csproj src/OpenHome.Formats/
COPY src/OpenHome.Server/OpenHome.Server.csproj src/OpenHome.Server/
RUN dotnet restore src/OpenHome.Server/OpenHome.Server.csproj
COPY src/ src/
RUN dotnet publish src/OpenHome.Server/OpenHome.Server.csproj \
    -c Release --no-restore -o /publish

# Stage 3: slim runtime — server + built web assets.
# Program.cs probes <content-root>/../../web/dist for the UI, so the layout
# below mirrors the repo: server at /app/src/OpenHome.Server, web at /app/web/dist.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app/src/OpenHome.Server
ENV ASPNETCORE_URLS=http://+:8080
# Data root (openhome.db, saves/, backups/) — mount a volume here.
ENV OPENHOME_DATA=/data
COPY --from=server /publish ./
COPY --from=web /build/web/dist /app/web/dist
VOLUME ["/data"]
EXPOSE 8080
ENTRYPOINT ["dotnet", "OpenHome.Server.dll"]
