FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder
WORKDIR /app
COPY . .
# Cloudsmith repos for overload-party are configured as public, so dotnet
# restore can read OverloadParty.GameDesignConstants without authentication.
# nuget.config points to the Cloudsmith feed.
RUN dotnet publish src/OverloadParty.Battle.Server -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=builder /app/publish .
# NPC AI 設定はコードと同梱でバージョン管理される app data のためイメージに含める
COPY --from=builder /app/src/OverloadParty.Battle.Npc/Data /app/NpcData
ENV NPC_AI_CONFIG_DIR=/app/NpcData
EXPOSE 9002
ENTRYPOINT ["dotnet", "OverloadParty.Battle.Server.dll"]
