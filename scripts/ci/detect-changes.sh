#!/usr/bin/env bash
# Detect package changes since last tag and compute next versions for the
# overload-party-battle repo (ADR-015 Phase 6 — battle-owned publish units).
#
# Package list (5 publish units after ADR-034 Phase 2):
#
#   Go modules (2 — distributed via GitHub git protocol + go module proxy):
#     - game-logic-constants-go  (github.com/kenyamaneko/overload-party-battle/packages/game-logic-constants-go)
#     - api-battle-rpc-go        (github.com/kenyamaneko/overload-party-battle/packages/api-battle-rpc-go)
#
#   C# csproj (1 — distributed via Cloudsmith NuGet):
#     - game-logic-constants-dotnet  (OverloadParty.GameLogicConstants)
#
#   npm packages (2 — distributed via Cloudsmith npm):
#     - game-logic-constants-npm  (@kenyamaneko/overload-party-game-logic-constants)
#     - game-state-npm            (@kenyamaneko/overload-party-game-state)
#
# api-battle-rpc-dotnet / game-state-dotnet は intra-repo の ProjectReference 専用で
# NuGet publish しない (battle 自身の sln 内のみで消費される)。
#
# Each published package has its own tag prefix "packages/{name}/v{version}".

set -euo pipefail

TARGET="${1:-auto}"
BUMP="${2:-patch}"

# (package_name:tag_prefix:watch_path:fallback_prefix)
# fallback_prefix は common 側の旧タグを参照するため "-" (該当なし) 固定。
# battle 独立化後は common の tag は見ない。
PACKAGES=(
  "game-logic-constants-go:packages/game-logic-constants-go:packages/game-logic-constants-go/:-"
  "api-battle-rpc-go:packages/api-battle-rpc-go:packages/api-battle-rpc-go/:-"
  "game-logic-constants-dotnet:packages/game-logic-constants-dotnet:packages/game-logic-constants-dotnet/:-"
  "game-logic-constants-npm:packages/game-logic-constants-npm:packages/game-logic-constants-npm/:-"
  "game-state-npm:packages/game-state-npm:packages/game-state-npm/:-"
)

compute_version() {
  local prefix="$1" bump="$2" fallback="$3"
  local last_tag
  last_tag=$(git tag -l "${prefix}/v*" | sort -V | tail -1)
  if [ -z "$last_tag" ] && [ "$fallback" != "-" ]; then
    last_tag=$(git tag -l "${fallback}/v*" | sort -V | tail -1)
    if [ -n "$last_tag" ]; then
      last_tag="${prefix}/v${last_tag#"${fallback}/v"}"
    fi
  fi
  if [ -z "$last_tag" ]; then
    echo "0.1.0"
    return
  fi
  local current major minor patch
  current="${last_tag#"${prefix}/v"}"
  IFS='.' read -r major minor patch <<< "$current"
  case "$bump" in
    major) echo "$((major + 1)).0.0" ;;
    minor) echo "${major}.$((minor + 1)).0" ;;
    *)     echo "${major}.${minor}.$((patch + 1))" ;;
  esac
}

has_changes() {
  local prefix="$1" path="$2" fallback="$3"
  local last_tag
  last_tag=$(git tag -l "${prefix}/v*" | sort -V | tail -1)
  if [ -z "$last_tag" ] && [ "$fallback" != "-" ]; then
    last_tag=$(git tag -l "${fallback}/v*" | sort -V | tail -1)
  fi
  if [ -z "$last_tag" ]; then
    return 0
  fi
  ! git diff --quiet "$last_tag" -- "$path"
}

CHANGED=()

for entry in "${PACKAGES[@]}"; do
  IFS=':' read -r name prefix path fallback <<< "$entry"

  case "$TARGET" in
    auto)
      if has_changes "$prefix" "$path" "$fallback"; then
        CHANGED+=("$name")
      fi
      ;;
    "$name")
      CHANGED+=("$name")
      ;;
  esac
done

for entry in "${PACKAGES[@]}"; do
  IFS=':' read -r name prefix _ fallback <<< "$entry"
  key=$(echo "$name" | tr '-' '_')

  if [[ " ${CHANGED[*]} " == *" $name "* ]]; then
    echo "${key}=true" >> "$GITHUB_OUTPUT"
    ver=$(compute_version "$prefix" "$BUMP" "$fallback")
    echo "${key}_version=$ver" >> "$GITHUB_OUTPUT"
    echo "$name → v$ver"
  else
    echo "${key}=false" >> "$GITHUB_OUTPUT"
  fi
done

ANY_CHANGED=false
if [ ${#CHANGED[@]} -gt 0 ]; then
  ANY_CHANGED=true
fi
echo "any_changed=$ANY_CHANGED" >> "$GITHUB_OUTPUT"
echo "changed_list=${CHANGED[*]:-}" >> "$GITHUB_OUTPUT"
