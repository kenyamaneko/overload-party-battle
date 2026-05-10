#!/usr/bin/env bash
# Start the Firestore emulator on localhost:9041 and wait for readiness.
# CI calls this with no arguments. Output is captured to /tmp/firestore.log so
# that timeouts can dump it for debugging.
set -euo pipefail

LOG=/tmp/firestore.log
gcloud emulators firestore start --host-port=127.0.0.1:9041 >"$LOG" 2>&1 &

for _ in {1..30}; do
  if curl -sf http://127.0.0.1:9041 >/dev/null; then
    echo "::notice::Firestore emulator ready on 127.0.0.1:9041"
    exit 0
  fi
  sleep 1
done

echo "::error::Firestore emulator failed to start within 30s"
cat "$LOG"
exit 1
