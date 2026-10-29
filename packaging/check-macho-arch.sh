#!/usr/bin/env bash
# AchievementCaretaker
# Copyright (c) 2026 Yaroslav V Tatarenko
#
# See LICENSE in the repository root.
#
#   ./check-macho-arch.sh <published-dir> <osx-x64|osx-arm64>
#
# Fails unless the apphost and the native libraries in a published macOS
# directory are all the architecture the runtime identifier asks for. The counterpart
# of check-pe-arch.sh: a bundle that mixes x86_64 and arm64 code starts and then dies
# loading the first library of the other kind, usually with a message that says nothing
# about architecture.
#
# Reads the Mach-O header itself, so it runs on Linux as well as on a Mac.

set -euo pipefail

dir="${1:-}"
rid="${2:-}"
if [ -z "$dir" ] || [ -z "$rid" ]; then
    echo "usage: check-macho-arch.sh <published-dir> <osx-x64|osx-arm64>" >&2
    exit 2
fi

case "$rid" in
    osx-x64)   want=16777223; want_name="x86_64" ;;
    osx-arm64) want=16777228; want_name="arm64" ;;
    *) echo "unknown runtime identifier: $rid" >&2; exit 2 ;;
esac

# Prints the CPU types a file holds, space separated, or "not-macho". A thin 64-bit
# little-endian Mach-O starts with the bytes CF FA ED FE and then the CPU type. A universal
# ("fat") file, which some of the native libraries are, starts with CA FE BA BE (big endian),
# a count, and one 20-byte entry per architecture whose first word is the CPU type.
hex_at() {
    od -An -tx1 -j "$2" -N 4 "$1" | tr -d '[:space:]'
}

macho_cpus() {
    local count i
    case "$(hex_at "$1" 0)" in
        cffaedfe)
            od -An -tu4 -j 4 -N 4 "$1" | tr -d '[:space:]'
            ;;
        cafebabe)
            count=$((16#$(hex_at "$1" 4)))
            for ((i = 0; i < count; i++)); do
                printf '%s ' "$((16#$(hex_at "$1" "$((8 + 20 * i))")))"
            done
            ;;
        *)
            echo "not-macho"
            ;;
    esac
}

name() {
    case "$1" in
        16777223) echo "x86_64" ;;
        16777228) echo "arm64" ;;
        *)        echo "$1" ;;
    esac
}

failed=0
checked=0
for f in "$dir"/AchievementCaretaker "$dir"/*.dylib; do
    [ -f "$f" ] || continue
    checked=$((checked + 1))
    got="$(macho_cpus "$f")"
    case " $got " in
        *" $want "*) ;;
        *)
            echo "  FAIL  $(basename "$f") holds [$(for c in $got; do printf "%s " "$(name "$c")"; done)], expected $want_name"
            failed=1
            ;;
    esac
done

if [ "$checked" -eq 0 ]; then
    echo "no Mach-O files found in $dir - was it published for $rid?" >&2
    exit 1
fi
if [ "$failed" -ne 0 ]; then
    exit 1
fi
echo "  $checked binaries in $dir are all $want_name, as $rid requires"
