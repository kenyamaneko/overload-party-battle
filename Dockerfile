FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder
ARG NUGET_TOKEN
WORKDIR /app
COPY . .
RUN dotnet nuget update source github --username x-access-token --password "${NUGET_TOKEN}" --store-password-in-clear-text \
    && dotnet publish src/OverloadParty.Battle.Server -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=builder /app/publish .
EXPOSE 9002
ENTRYPOINT ["dotnet", "OverloadParty.Battle.Server.dll"]
