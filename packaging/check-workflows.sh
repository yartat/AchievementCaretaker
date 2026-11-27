#!/usr/bin/env bash
# AchievementCaretaker
# Copyright (c) 2026 Yaroslav V Tatarenko
#
# See LICENSE in the repository root.
#
#   bash packaging/check-workflows.sh [<workflow directory>]
#
# Fails when a workflow uses an action by a movable tag, hands the tag name to a script other
# than through env:, or installs a Python package without naming its version. The release
# jobs hold a write token and the publishing keys.

set -euo pipefail

dir="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/.github/workflows}"
failed=0

report() {
    echo "  FAIL  $1"
    failed=1
}

for file in "$dir"/*.yml; do
    name="$(basename "$file")"

    while IFS= read -r line; do
        report "$name:$line: an action must be pinned to a commit hash"
    done < <(grep -nE '^[[:space:]]*(- )?uses:' "$file" | grep -vE '@[0-9a-f]{40}([[:space:]]|$)' | tr -d '\r' || true)

    while IFS= read -r line; do
        report "$name:$line: the tag name goes into env:, never into a script"
    done < <(grep -nF 'github.ref_name' "$file" | grep -vE '^[0-9]+:[[:space:]]*[A-Z_]+:[[:space:]]*\$\{\{ github\.ref_name \}\}' | tr -d '\r' || true)

    while IFS= read -r line; do
        report "$name:$line: a Python package needs a version"
    done < <(grep -nE '^[^#]*pip install' "$file" | grep -vE '==|-r ' | tr -d '\r' || true)
done

if [ "$failed" -ne 0 ]; then
    exit 1
fi
echo "workflows are pinned"
