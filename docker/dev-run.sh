#!/bin/sh
# battle を SDK コンテナ内で起動する。OverloadParty.* の NuGet は Cloudsmith の
# 匿名 feed から取得するため、restore 前に source を登録する (既存ならスキップ)。
set -eu

dotnet nuget list source | grep -q cloudsmith-overload-party-nuget \
  || dotnet nuget add source https://nuget.cloudsmith.io/keyandnotes/overload-party-nuget/v3/index.json --name cloudsmith-overload-party-nuget

exec dotnet run --project src/OverloadParty.Battle.Server
