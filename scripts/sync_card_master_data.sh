#!/usr/bin/env bash
# card が配布するカード定義・施策定義を battle 同梱のキャッシュへ取り込み、取り込み元の
# コミットを card_source_gen.json に記録する。
# キャッシュの中身を git オブジェクトとして取り出し、同じコミットから記録を書くため、
# 記録と中身がずれない。
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CARD_REPO="${CARD_REPO:-$REPO_ROOT/../overload-party-card}"
CARD_REF="${CARD_REF:-origin/main}"

CACHE_DIR="$REPO_ROOT/packages/game-state-dotnet/cache"
CARD_CACHE_DIR="data/cache"
SOURCE_RECORD="$CACHE_DIR/card_source_gen.json"
MASTER_DATA_FILES=(cards_gen.json initiatives_gen.json)

if [ ! -e "$CARD_REPO/.git" ]; then
  echo "card repository not found at $CARD_REPO. Set CARD_REPO to a clone of overload-party-card." >&2
  exit 1
fi

git -C "$CARD_REPO" fetch --quiet origin

commit="$(git -C "$CARD_REPO" rev-parse --verify "$CARD_REF^{commit}")"
committed_at="$(TZ=UTC git -C "$CARD_REPO" log -1 --format=%cd --date=iso-strict-local "$commit")"
repository="$(git -C "$CARD_REPO" remote get-url origin | sed -E 's#^.*github\.com[:/]##; s#\.git$##')"
fetched_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

for file in "${MASTER_DATA_FILES[@]}"; do
  git -C "$CARD_REPO" show "$commit:$CARD_CACHE_DIR/$file" >"$CACHE_DIR/$file"
done

printf '{\n  "repository": "%s",\n  "commit": "%s",\n  "committed_at": "%s",\n  "fetched_at": "%s"\n}\n' \
  "$repository" "$commit" "$committed_at" "$fetched_at" >"$SOURCE_RECORD"

echo "Synced ${MASTER_DATA_FILES[*]} from $repository@$commit ($committed_at)"
echo "Recorded the source in ${SOURCE_RECORD#"$REPO_ROOT"/}"
