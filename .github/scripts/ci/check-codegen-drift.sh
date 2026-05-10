#!/usr/bin/env bash
# packages/ 配下の生成ファイルが data/ と乖離していないかを検証する。
# .codegen-views/ は split_openapi_for_dotnet.py が書き出す一時ディレクトリで意図的に未追跡。
set -euo pipefail

git status --porcelain -- packages/ | grep -v '^?? \.codegen-views/' >/tmp/drift.txt || true

if [ -s /tmp/drift.txt ]; then
  echo "::error::Generated files drifted from data/. Run scripts/generate_types.sh and commit."
  cat /tmp/drift.txt
  git --no-pager diff -- packages/ | head -200
  exit 1
fi

echo "::notice::No drift detected in generated files."
