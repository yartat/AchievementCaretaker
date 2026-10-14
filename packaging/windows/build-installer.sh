#!/usr/bin/env bash
# AchievementCaretaker
# Copyright (c) 2026 Yaroslav V Tatarenko
#
# See LICENSE in the repository root.
#
# Builds the Windows installer from an already-published directory.
#
#   ./build-installer.sh --publish-dir out/win-x64 --version 1.0.0 --arch x64
#
# The publish must be SELF-CONTAINED. The installer offers no way to fetch the
# .NET runtime, and an installed application that reports a missing runtime is
# a worse first run than a larger download.
#
# Needs: makensis (Debian/Ubuntu package `nsis`). No Windows machine and no Wine
# are involved - NSIS cross-compiles the installer stub.

set -euo pipefail

APPHOST="AchievementCaretaker.exe"
publish_dir=""
version=""
arch=""
output_dir="dist"
homepage="${WIN_HOMEPAGE:-https://github.com/yartat/AchievementCaretaker}"

while [ $# -gt 0 ]; do
    case "$1" in
        --publish-dir) publish_dir="$2"; shift 2 ;;
        --version)     version="$2";     shift 2 ;;
        --arch)        arch="$2";        shift 2 ;;
        --output)      output_dir="$2";  shift 2 ;;
        --homepage)    homepage="$2";    shift 2 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

if [ -z "$publish_dir" ] || [ -z "$version" ] || [ -z "$arch" ]; then
    echo "usage: build-installer.sh --publish-dir <dir> --version <x.y.z> --arch <x64|x86> [--output <dir>]" >&2
    exit 2
fi
case "$arch" in
    x64|x86) ;;
    *) echo "unknown architecture: $arch" >&2; exit 2 ;;
esac
if [ ! -f "$publish_dir/$APPHOST" ]; then
    echo "no $APPHOST in $publish_dir" >&2
    exit 1
fi
if [ ! -f "$publish_dir/System.Private.CoreLib.dll" ]; then
    echo "$publish_dir looks framework-dependent; publish with --self-contained true" >&2
    exit 1
fi

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"

# VIProductVersion insists on four numeric parts; the project version has three,
# and a "~dev" suffix on a development build has to come off entirely.
version4="${version%%[-+~]*}.0"

mkdir -p "$output_dir"
outfile="$output_dir/achievement-caretaker-${version}-win-${arch}-setup.exe"

# NSIS resolves relative paths against its own working directory, so everything
# handed over is absolute.
makensis -NOCD -V2 \
    "-DVERSION=$version" \
    "-DVERSION4=$version4" \
    "-DARCH=$arch" \
    "-DPUBLISH_DIR=$(cd "$publish_dir" && pwd)" \
    "-DICON=$root/AchievementCaretaker.App/Resources/AchievementCaretaker.ico" \
    "-DLICENSE_FILE=$root/LICENSE.txt" \
    "-DHOMEPAGE=$homepage" \
    "-DOUTFILE=$(cd "$output_dir" && pwd)/$(basename "$outfile")" \
    "$here/installer.nsi"

if [ ! -f "$outfile" ]; then
    echo "makensis reported success but produced no $outfile" >&2
    exit 1
fi

echo "built $outfile ($(du -h "$outfile" | cut -f1))"
