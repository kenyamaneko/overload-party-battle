#!/usr/bin/env bash
# 直近のタグからの変更を検出して、各 publish unit の次バージョンを算出する。
# 各 publish unit は "packages/{name}/v{version}" のタグプレフィックスを持つ。

set -euo pipefail

TARGET="$1"
BUMP="$2"

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
    patch) echo "${major}.${minor}.$((patch + 1))" ;;
    *)     echo "unknown bump level: ${bump}" >&2; exit 1 ;;
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
