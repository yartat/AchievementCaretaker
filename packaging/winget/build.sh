#!/usr/bin/env bash
# AchievementCaretaker
# Copyright (c) 2026 Yaroslav V Tatarenko
#
# See LICENSE in the repository root.
#
# Builds the winget manifests for a published release: three files under
# manifests/y/yartat/AchievementCaretaker/<version>/, ready for a pull
# request to microsoft/winget-pkgs. The installers are the NSIS setup.exe files from
# the release; winget downloads them and checks them by SHA-256.
#
#   bash packaging/winget/build.sh 2.1.0 [--out dist/winget]
#
# Nothing is submitted here. Check the result with `winget validate`, then open the
# pull request yourself or with wingetcreate.

set -euo pipefail

version="${1:-}"
if [ -z "$version" ]; then
    echo "usage: build.sh <x.y.z> [--out <dir>]" >&2
    exit 2
fi
shift

out="dist/winget"
while [ $# -gt 0 ]; do
    case "$1" in
        --out) out="$2"; shift 2 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
here="$root/packaging/winget"
base="https://github.com/yartat/AchievementCaretaker/releases/download/v$version"
url32="$base/achievement-caretaker-$version-win-x86-setup.exe"
url64="$base/achievement-caretaker-$version-win-x64-setup.exe"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

echo "downloading the two setup.exe files of $version"
curl -fsSL -o "$work/x86.exe" "$url32"
curl -fsSL -o "$work/x64.exe" "$url64"
sha32="$(sha256sum "$work/x86.exe" | cut -d' ' -f1)"
sha64="$(sha256sum "$work/x64.exe" | cut -d' ' -f1)"
echo "  x86 $sha32"
echo "  x64 $sha64"

dir="$out/manifests/y/yartat/AchievementCaretaker/$version"
mkdir -p "$dir"

# Strips the template's own comment lines, fills in the fields, and puts the schema
# header first, which winget uses to check the file.
render() {
    local schema="https://aka.ms/winget-manifest.$1.1.12.0.schema.json"
    {
        echo "# yaml-language-server: \$schema=$schema"
        grep -v '^#' "$2" \
            | sed -e "s|@VERSION@|$version|g" \
                  -e "s|@URL32@|$url32|g" \
                  -e "s|@URL64@|$url64|g" \
                  -e "s|@SHA32@|$sha32|g" \
                  -e "s|@SHA64@|$sha64|g"
    } > "$3"
}

render version "$here/version.yaml.in" "$dir/yartat.AchievementCaretaker.yaml"
render defaultLocale "$here/defaultLocale.yaml.in" "$dir/yartat.AchievementCaretaker.locale.en-US.yaml"
render installer "$here/installer.yaml.in" "$dir/yartat.AchievementCaretaker.installer.yaml"

if grep -q '@[A-Z0-9]*@' "$dir"/*.yaml; then
    echo "a template field was not filled in" >&2
    exit 1
fi

echo "manifests written to $dir"
ls -l "$dir"
