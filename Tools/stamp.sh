#!/bin/sh
# Prints a hash of the code and data in the working tree, tracked or new, excluding Unity's generated .meta files.
cd "$(git rev-parse --show-toplevel)" || exit 1
git ls-files -c -o --exclude-standard -- Assets/_Game Assets/StreamingAssets/Data \
    | grep -v '\.meta$' | LC_ALL=C sort \
    | while IFS= read -r f; do [ -f "$f" ] && git hash-object --no-filters -- "$f"; done \
    | git hash-object --stdin
