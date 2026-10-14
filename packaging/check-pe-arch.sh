#!/usr/bin/env bash
# AchievementCaretaker
# Copyright (c) 2026 Yaroslav V Tatarenko
#
# See LICENSE in the repository root.
#
#   ./check-pe-arch.sh <published-dir> <win-x64|win-x86|win-arm64>
#
# Fails unless the apphost and the native libraries in a published Windows
# directory are all the architecture the runtime identifier asks for.
#
# This exists because getting it wrong is silent. `dotnet publish -r win-x86`
# alone resolves 32-bit native libraries but can still emit a 64-bit apphost;
# the result runs far enough to load SkiaSharp and then dies in its type
# initializer, with nothing on stdout or stderr to say why. Building 32-bit
# needs -p:Platform=x86 as well as -r win-x86 - see docs/BUILD.md.

set -euo pipefail

dir="${1:-}"
rid="${2:-}"
if [ -z "$dir" ] || [ -z "$rid" ]; then
    echo "usage: check-pe-arch.sh <published-dir> <win-x64|win-x86|win-arm64>" >&2
    exit 2
fi

case "$rid" in
    win-x64)   want=34404; want_name="x64" ;;
    win-x86)   want=332;   want_name="i386" ;;
    win-arm64) want=43620; want_name="arm64" ;;
    *) echo "unknown runtime identifier: $rid" >&2; exit 2 ;;
esac

# The COFF machine word sits two bytes past the PE signature, which e_lfanew at
# offset 60 points at.
pe_machine() {
    local lfanew
    lfanew="$(od -An -tu4 -j 60 -N 4 "$1" | tr -d ' ')"
    od -An -tu2 -j "$((lfanew + 4))" -N 2 "$1" | tr -d ' '
}

name() {
    case "$1" in
        34404) echo "x64" ;;
        332)   echo "i386" ;;
        43620) echo "arm64" ;;
        *)     echo "machine=$1" ;;
    esac
}

failed=0
checked=0
for f in "$dir"/AchievementCaretaker.exe \
         "$dir"/libSkiaSharp.dll "$dir"/libHarfBuzzSharp.dll \
         "$dir"/av_libglesv2.dll "$dir"/e_sqlite3.dll; do
    [ -f "$f" ] || continue
    checked=$((checked + 1))
    got="$(pe_machine "$f")"
    if [ "$got" != "$want" ]; then
        echo "  FAIL  $(basename "$f") is $(name "$got"), expected $want_name"
        failed=1
    fi
done

if [ "$checked" -eq 0 ]; then
    echo "nothing to check in $dir - is that a published Windows directory?" >&2
    exit 1
fi

if [ "$failed" -ne 0 ]; then
    echo "$dir does not match $rid; a mixed-architecture build dies in SkiaSharp with no message" >&2
    exit 1
fi

echo "  $checked binaries in $dir are all $want_name, as $rid requires"
