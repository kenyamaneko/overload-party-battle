#!/usr/bin/env bash
# data/openapi.yaml から Go / C# / TS の型を再生成し、game_logic_constants.yaml の
# 定数生成も併走させる。C# は x-battle-csharp-package 拡張で 2 ビューに分割してから NSwag に渡す。
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

VIEWS_DIR="$REPO_ROOT/.codegen-views"
mkdir -p "$VIEWS_DIR"

echo "::group::Split openapi.yaml into per-package C# views"
python scripts/split_openapi_for_dotnet.py \
  --input data/openapi.yaml \
  --rpc-out "$VIEWS_DIR/openapi.rpc.yaml" \
  --state-out "$VIEWS_DIR/openapi.state.yaml"
echo "::endgroup::"

echo "::group::Generate Go types (oapi-codegen)"
(
  cd packages/api-battle-rpc-go
  oapi-codegen -config openapi-codegen.yaml ../../data/openapi.yaml
)
echo "::endgroup::"

echo "::group::Generate C# RPC types (NSwag)"
(
  cd packages/api-battle-rpc-dotnet
  nswag run nswag.json
)
echo "::endgroup::"

echo "::group::Generate C# game-state types (NSwag)"
(
  cd packages/game-state-dotnet
  nswag run nswag.json
)
echo "::endgroup::"

echo "::group::Generate TS types (openapi-typescript)"
npx --yes openapi-typescript@7 \
  data/openapi.yaml \
  --output packages/game-state-npm/src/openapi.gen.ts
echo "::endgroup::"

echo "::group::Run game-logic-constants codegen"
python scripts/generate_constants.py
echo "::endgroup::"
