# Build context = workspace root (needs Shared/). Build:
#   docker build -f MiniToolsHub/deploy/Dockerfile -t minitools-hub:latest .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Shared/MiniTools.Contracts/ Shared/MiniTools.Contracts/
COPY MiniToolsHub/src/MiniToolsHub.Server/ MiniToolsHub/src/MiniToolsHub.Server/
RUN dotnet restore MiniToolsHub/src/MiniToolsHub.Server/MiniToolsHub.Server.csproj
RUN dotnet publish MiniToolsHub/src/MiniToolsHub.Server/MiniToolsHub.Server.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /out .
COPY MiniToolsHub/content /app/content
ENV ASPNETCORE_URLS=http://+:18473 \
    Hub__DataDir=/data \
    Hub__ContentDir=/app/content
# /data holds hub.db and the uploaded zips; mount a volume there (owned by uid 1654 = "app").
RUN mkdir -p /data && chown app:app /data
USER app
EXPOSE 18473
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD ["dotnet", "MiniToolsHub.Server.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "MiniToolsHub.Server.dll"]
