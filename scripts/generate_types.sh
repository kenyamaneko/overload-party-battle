#!/usr/bin/env bash
# generate_types.sh — data/openapi.yaml から各言語の型を再生成する。
#
#   - Go (oapi-codegen)            → packages/api-battle-rpc-go/openapi_gen.go
#   - C# (NSwag) RPC view          → packages/api-battle-rpc-dotnet/BattleRpc_gen.cs
#   - C# (NSwag) game-state view   → packages/game-state-dotnet/GameState_gen.cs
#   - TS (openapi-typescript)      → packages/game-state-npm/src/openapi.gen.ts
#
# C# は SSoT 単一 yaml を `x-battle-csharp-package` 拡張で 2 ビューに分割してから NSwag に渡す。
# ゲームロジック定数 (data/game_logic_constants.yaml) は本 ADR scope 外で
# `python -m overload_party_codegen_tools` 経由の現行 codegen を維持する。
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

echo "::group::Run game-logic-constants codegen (out of scope but kept)"
python scripts/generate_constants.py
echo "::endgroup::"
