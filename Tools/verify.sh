#!/bin/sh
# Runs every EditMode test in the open Editor. If all pass, records a stamp of the code and data they ran on.
# The pre-commit hook refuses a commit whose code or data differs from the stamp.
cd "$(git rev-parse --show-toplevel)" || exit 1
out=$(mktemp)
unity recompile --timeout 200 --no-banner --format json > /dev/null 2>&1
# The reload starts just after the recompile returns; tests asked for during it do not run.
sleep 5
for i in $(seq 1 70); do
    unity status --no-banner --format json 2>/dev/null | grep -q '"state": *"ready"' && break
    sleep 8
done
unity command test run --mode editor --timeout 590 --no-banner --format json > "$out" 2>&1
python - "$out" <<'EOF'
import json, sys
doc, _ = json.JSONDecoder().raw_decode(open(sys.argv[1], encoding="utf-8").read())
summary = ((doc.get("data") or {}).get("result") or {}).get("Summary") or {}
print(summary)
for test in doc["data"]["result"]["Results"] if summary else []:
    if test["Status"] not in ("Passed", "Skipped"):
        print("-", test["FullName"])
sys.exit(0 if doc.get("success") and summary.get("Total", 0) > 0 and summary.get("Failed") == 0 and summary.get("Inconclusive", 0) == 0 else 1)
EOF
status=$?
rm -f "$out"
if [ $status -ne 0 ]; then
    rm -f "$(git rev-parse --git-dir)/fes-tests-passed"
    echo "Tests did not pass; no stamp." >&2
    exit 1
fi
sh Tools/stamp.sh > "$(git rev-parse --git-dir)/fes-tests-passed"
echo "Stamp recorded."
