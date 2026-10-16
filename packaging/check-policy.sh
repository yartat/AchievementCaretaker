#!/usr/bin/env bash
# AchievementCaretaker
# Copyright (c) 2026 Yaroslav V Tatarenko
#
# See LICENSE in the repository root.
#
# Checks the documents every release must carry, and puts them into a publish
# directory so that the installer and the portable archive ship them.
#
#   bash packaging/check-policy.sh                 # check only
#   bash packaging/check-policy.sh out/win-x64     # check, then copy into the publish
#
# Fails unless PRIVACY.md and LICENSE.txt exist, PRIVACY.md is not empty, and the
# README links to PRIVACY.md. A publish directory that does not end up holding the
# policy is refused too.

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
publish_dir="${1:-}"
failed=0

fail() {
    echo "  FAIL  $1" >&2
    failed=1
}

for file in PRIVACY.md LICENSE.txt; do
    if [ ! -s "$root/$file" ]; then
        fail "$file is missing or empty"
    fi
done

if ! grep -qF "](PRIVACY.md)" "$root/README.md"; then
    fail "README.md does not link to PRIVACY.md"
fi

if [ "$failed" -ne 0 ]; then
    exit 1
fi

if [ -n "$publish_dir" ]; then
    if [ ! -d "$publish_dir" ]; then
        echo "  FAIL  no publish directory at $publish_dir" >&2
        exit 1
    fi
    cp "$root/PRIVACY.md" "$root/LICENSE.txt" "$publish_dir/"
    for file in PRIVACY.md LICENSE.txt; do
        if ! cmp -s "$root/$file" "$publish_dir/$file"; then
            echo "  FAIL  $publish_dir/$file does not match $file" >&2
            exit 1
        fi
    done
    echo "  ok    PRIVACY.md and LICENSE.txt are in $publish_dir"
fi

echo "policy documents are present and linked"
