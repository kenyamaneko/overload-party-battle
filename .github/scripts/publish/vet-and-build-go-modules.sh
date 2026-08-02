#!/usr/bin/env bash
set -euo pipefail

modules=(
  packages/game-logic-constants-go
  packages/api-battle-rpc-go
)

for mod in "${modules[@]}"; do
  echo "::group::go vet ${mod}"
  (cd "${mod}" && go vet ./... && go build ./...)
  echo "::endgroup::"
done
