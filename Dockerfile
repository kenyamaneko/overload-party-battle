FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder
WORKDIR /app
COPY . .
RUN dotnet publish src/OverloadParty.Battle.Server -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=builder /app/publish .
COPY --from=builder /app/internal/cache/cards_gen.json /app/data/cards_gen.json
EXPOSE 9001
ENTRYPOINT ["dotnet", "OverloadParty.Battle.Server.dll"]
