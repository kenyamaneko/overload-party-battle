#!/usr/bin/env bash
set -euo pipefail

: "${PACKAGE:?PACKAGE env required}"
: "${VERSION:?VERSION env required}"

tag="packages/${PACKAGE}/v${VERSION}"
echo "::notice::tagging ${tag}"
git tag "${tag}"
git push origin "${tag}"
