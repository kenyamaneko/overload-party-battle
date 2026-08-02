#!/usr/bin/env bash
set -euo pipefail

npm install
npm run build -w @kenyamaneko/overload-party-game-logic-constants
npm run build -w @kenyamaneko/overload-party-game-state
