FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder
WORKDIR /app
COPY . .
# CI は runner 上で Cloudsmith source を設定するが、その設定は隔離された docker
# build には届かない。OverloadParty.GameDesignConstants を持つ public feed を
# コンテナ内でも匿名登録してから復元する。
RUN dotnet nuget add source \
      https://nuget.cloudsmith.io/keyandnotes/overload-party-nuget/v3/index.json \
      --name cloudsmith-overload-party-nuget
RUN dotnet publish src/OverloadParty.Battle.Server -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=builder /app/publish .
# NPC AI 設定はコードと同梱でバージョン管理される app data のためイメージに含める
COPY --from=builder /app/src/OverloadParty.Battle.Npc/Data /app/NpcData
ENV NPC_AI_CONFIG_DIR=/app/NpcData
EXPOSE 9002
ENTRYPOINT ["dotnet", "OverloadParty.Battle.Server.dll"]
