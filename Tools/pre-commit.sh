#!/bin/sh
# Refuses a commit that repeats a known mistake. Install: sh Tools/install-hooks.sh
cd "$(git rev-parse --show-toplevel)" || exit 1
staged=$(git diff --cached --name-only --diff-filter=ACMR)
added=$(git diff --cached --name-only --diff-filter=A)
fail=0
refuse() { echo "commit refused: $1" >&2; fail=1; }

# Tests must have passed on exactly this code and data.
if echo "$staged" | grep -qE '^Assets/(_Game|StreamingAssets/Data)/'; then
    stamp_file="$(git rev-parse --git-dir)/fes-tests-passed"
    if [ ! -f "$stamp_file" ] || [ "$(cat "$stamp_file")" != "$(sh Tools/stamp.sh)" ]; then
        refuse "code or data changed since the tests last passed; run: sh Tools/verify.sh"
    fi
    # Unstaged changes would mean the stamp covers something other than the commit.
    if git diff --name-only -- Assets/_Game Assets/StreamingAssets/Data | grep -q .; then
        refuse "code or data has unstaged changes the tests ran on"
    fi
fi

for f in $(echo "$staged" | grep -E '^(\.serena/|Assets/_Recovery)|/Temp[^/]*\.cs(\.meta)?$' | tr ' ' '?'); do
    refuse "$f must not be committed"
done

# A new asset without its .meta gets a new GUID on every other machine.
echo "$added" | grep -E '^Assets/' | grep -v '\.meta$' | while IFS= read -r f; do
    git cat-file -e ":$f.meta" 2>/dev/null || echo "$f"
done > "$(git rev-parse --git-dir)/fes-no-meta"
if [ -s "$(git rev-parse --git-dir)/fes-no-meta" ]; then
    refuse "no .meta staged for: $(cat "$(git rev-parse --git-dir)/fes-no-meta")"
fi

# New golden hashes change behaviour, which needs a recorded decision (DEC-108).
if echo "$staged" | grep -q 'GoldenRunTests\.cs$' && ! echo "$staged" | grep -q '^Docs/GDD/CHANGELOG\.md$'; then
    refuse "golden hashes changed without Docs/GDD/CHANGELOG.md"
fi

# A review is kept in two languages.
for en in $(echo "$staged" | grep -E '^Docs/GDD/reviews/.*\.md$' | grep -v '\.ko\.md$'); do
    ko="${en%.md}.ko.md"
    git cat-file -e "HEAD:$ko" 2>/dev/null && ! echo "$staged" | grep -qF "$ko" && refuse "$en staged without $ko"
done

exit $fail
