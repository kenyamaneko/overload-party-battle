#!/usr/bin/env bash
# packages/ 配下の tracked な生成ファイルが data/ と乖離していないかを検証する。
# CI の codegen ステップ直後に実行し、tracked ファイルに差分があれば fail する。
# untracked な生成ファイル (.codegen-views や gitignore 対象の NSwag 出力) は対象外。
set -euo pipefail

if ! git diff --quiet -- packages/; then
  echo "::error::Generated files drifted from data/. Run scripts/generate_types.sh and commit."
  git --no-pager diff -- packages/ | head -200
  exit 1
fi

echo "::notice::No drift detected in generated files."
