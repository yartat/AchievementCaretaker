#!/usr/bin/env bash
# AchievementCaretaker
# Copyright (c) 2026 Yaroslav V Tatarenko
#
# See LICENSE in the repository root.
#
# Builds the Chocolatey package for a published release. The package downloads the
# portable zips from the GitHub release and checks them by SHA-256; it contains no
# binaries of its own.
#
#   bash packaging/chocolatey/build.sh 2.1.0 [--out dist/chocolatey] [--push]
#
# --push sends the package to the community repository. It reads the API key from
# CHOCO_API_KEY and never writes it anywhere else.
#
# Needs choco on PATH and the release to be published first, because the hashes are
# taken from the files the release serves.

set -euo pipefail

version="${1:-}"
if [ -z "$version" ]; then
    echo "usage: build.sh <x.y.z> [--out <dir>]" >&2
    exit 2
fi
shift

out="dist/chocolatey"
push=0
while [ $# -gt 0 ]; do
    case "$1" in
        --out) out="$2"; shift 2 ;;
        --push) push=1; shift ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
here="$root/packaging/chocolatey"
base="https://github.com/yartat/AchievementCaretaker/releases/download/v$version"
url32="$base/achievement-caretaker-$version-win-x86-portable.zip"
url64="$base/achievement-caretaker-$version-win-x64-portable.zip"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

echo "downloading the two portable zips of $version"
curl -fsSL -o "$work/x86.zip" "$url32"
curl -fsSL -o "$work/x64.zip" "$url64"
sha32="$(sha256sum "$work/x86.zip" | cut -d' ' -f1)"
sha64="$(sha256sum "$work/x64.zip" | cut -d' ' -f1)"
echo "  x86 $sha32"
echo "  x64 $sha64"

# Everything the package contains is staged here, so the .in templates never ship.
stage="$work/stage"
mkdir -p "$stage/tools"

render() {
    sed -e "s|@VERSION@|$version|g" \
        -e "s|@URL32@|$url32|g" \
        -e "s|@URL64@|$url64|g" \
        -e "s|@SHA32@|$sha32|g" \
        -e "s|@SHA64@|$sha64|g" "$1" > "$2"
}

render "$here/achievement-caretaker.nuspec.in" "$stage/achievement-caretaker.nuspec"
render "$here/tools/chocolateyInstall.ps1.in" "$stage/tools/chocolateyInstall.ps1"
cp "$here/tools/chocolateyUninstall.ps1" "$stage/tools/chocolateyUninstall.ps1"

if grep -q '@[A-Z0-9]*@' "$stage/achievement-caretaker.nuspec" "$stage/tools/chocolateyInstall.ps1"; then
    echo "a template field was not filled in" >&2
    exit 1
fi

mkdir -p "$out"
outdir="$out"
if command -v cygpath >/dev/null 2>&1; then
    stage_arg="$(cygpath -w "$stage/achievement-caretaker.nuspec")"
    outdir="$(cygpath -w "$(cd "$out" && pwd)")"
else
    stage_arg="$stage/achievement-caretaker.nuspec"
fi

choco pack "$stage_arg" --outputdirectory "$outdir"
nupkg="$out/achievement-caretaker.$version.nupkg"
ls -l "$nupkg"

if [ "$push" -eq 1 ]; then
    : "${CHOCO_API_KEY:?CHOCO_API_KEY must be set to push}"
    choco push "$nupkg" --source https://push.chocolatey.org/ --api-key "$CHOCO_API_KEY"
fi
