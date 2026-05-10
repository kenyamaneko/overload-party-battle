#!/usr/bin/env bash
# Detect breaking changes in data/openapi.yaml against the PR base ref.
# Usage: openapi-diff.sh <base-ref>
#
# Skips silently with a notice if the base ref does not yet contain
# data/openapi.yaml (e.g. before this Phase 2 PR merges). After merge the
# job runs the standard comparison and fails on any breaking change.
set -euo pipefail

BASE_REF="${1:-}"
if [ -z "$BASE_REF" ]; then
  echo "::error::usage: openapi-diff.sh <base-ref>"
  exit 1
fi

if ! git cat-file -e "${BASE_REF}:data/openapi.yaml" 2>/dev/null; then
  echo "::notice::data/openapi.yaml does not exist on ${BASE_REF}; skipping breaking-change diff (initial introduction)."
  exit 0
fi

git show "${BASE_REF}:data/openapi.yaml" >/tmp/openapi.base.yaml

oasdiff breaking /tmp/openapi.base.yaml data/openapi.yaml --fail-on ERR
