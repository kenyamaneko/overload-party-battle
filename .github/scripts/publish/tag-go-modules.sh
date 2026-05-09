#!/usr/bin/env bash
# Tag Go modules under packages/ using semantic versions computed by detect-changes.sh.
#
# Inputs (env):
#   GLC_GO_CHANGED  / GLC_GO_VERSION   game-logic-constants-go の変更フラグ + 新バージョン
#   ABR_GO_CHANGED  / ABR_GO_VERSION   api-battle-rpc-go の変更フラグ + 新バージョン
set -euo pipefail

modules=(
  "game-logic-constants-go:${GLC_GO_CHANGED:-false}:${GLC_GO_VERSION:-}"
  "api-battle-rpc-go:${ABR_GO_CHANGED:-false}:${ABR_GO_VERSION:-}"
)

for entry in "${modules[@]}"; do
  IFS=':' read -r name changed version <<< "$entry"
  if [ "$changed" != "true" ]; then
    echo "::notice::skipping $name (no change)"
    continue
  fi
  if [ -z "$version" ]; then
    echo "::error::$name marked changed but no version computed"
    exit 1
  fi
  tag="packages/$name/v$version"
  echo "::notice::tagging $tag"
  git tag "$tag"
  git push origin "$tag"
done
