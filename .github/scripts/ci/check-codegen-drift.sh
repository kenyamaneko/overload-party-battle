#!/usr/bin/env bash
# Verify that generated code in packages/ matches what scripts/generate_types.sh
# produces from data/openapi.yaml + data/game_logic_constants.yaml. Run after
# the codegen step in CI; fail the build if any tracked generated file changed.
set -euo pipefail

# .codegen-views/ is a transient working directory written by
# scripts/split_openapi_for_dotnet.py. It is intentionally not tracked.
git status --porcelain -- packages/ | grep -v '^?? \.codegen-views/' >/tmp/drift.txt || true

if [ -s /tmp/drift.txt ]; then
  echo "::error::Generated files drifted from data/. Run scripts/generate_types.sh and commit."
  cat /tmp/drift.txt
  git --no-pager diff -- packages/ | head -200
  exit 1
fi

echo "::notice::No drift detected in generated files."
