#!/usr/bin/env bash
# Fails when any .rs file under engine/ exceeds 1,000 lines.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
status=0
while IFS= read -r f; do
  n=$(wc -l < "$f")
  if [ "$n" -gt 1000 ]; then echo "too long ($n lines): $f"; status=1; fi
done < <(find "$root" -name '*.rs' -not -path '*/target/*')
exit $status
