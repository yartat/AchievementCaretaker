#!/usr/bin/env bash
# AchievementCaretaker
# Copyright (c) 2026 Yaroslav V Tatarenko
#
# See LICENSE in the repository root.
#
# Fails unless every place that states a version agrees with the one given.
#
# The version lives in more places than it should: the projects, and a
# window-title literal that is not generated from the assembly version. Rather
# than paper over that in CI by overriding the version from the tag - which
# would leave the title saying something else - this refuses to release until
# the repository and the tag say the same thing.

set -euo pipefail

version="${1:-}"
if [ -z "$version" ]; then
    echo "usage: check-version.sh <x.y.z>" >&2
    exit 2
fi

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
assembly="${version%%[-+~]*}.0"          # 1.0.0 -> 1.0.0.0
title="$(echo "$version" | cut -d. -f1,2)"  # 1.0.0 -> 1.0
failed=0

expect() {
    local label="$1" file="$2" pattern="$3"
    if grep -qF -- "$pattern" "$root/$file"; then
        echo "  ok    $label"
    else
        echo "  FAIL  $label: $file does not contain '$pattern'"
        failed=1
    fi
}

echo "checking that the repository says $version"

# The version is declared once, in Directory.Build.props, for every project.
expect "Version"         "Directory.Build.props" "<Version>$version</Version>"
expect "AssemblyVersion" "Directory.Build.props" "<AssemblyVersion>$assembly</AssemblyVersion>"
expect "FileVersion"     "Directory.Build.props" "<FileVersion>$assembly</FileVersion>"

expect "library window title" \
    "AchievementCaretaker.App/Views/GamePickerWindow.axaml" \
    "Achievement Caretaker $title |"

expect "release notes entry" "docs/RELEASE-NOTES.md" "## $version "

if [ "$failed" -ne 0 ]; then
    cat >&2 <<EOF

The tag and the repository disagree. Update the places listed above - the
release checklist in docs/BUILD.md lists them - and move the tag.
EOF
    exit 1
fi

echo "all version references agree"
