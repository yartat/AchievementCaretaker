#!/usr/bin/env bash
# AchievementCaretaker
# Copyright (c) 2026 Yaroslav V Tatarenko
#
# See LICENSE in the repository root.
#
# Builds "Achievement Caretaker.app" and a zip of it from an already-published
# osx-x64 or osx-arm64 directory.
#
#   bash build-app.sh --publish-dir out/osx-arm64 --version 2.2.0 --arch arm64 --output dist
#
# The publish must be SELF-CONTAINED, for the same reason as every other artifact:
# nothing has to be installed first.
#
# Run it on a Mac (the release workflow uses a macOS runner). It needs codesign, ditto,
# sips and iconutil, which a Mac has. The application is signed AD HOC (`codesign -s -`):
# that is not a Developer ID signature and Gatekeeper still asks the user to approve the
# first start, but an arm64 binary with no signature at all is killed by the kernel, so
# even this is required. There is no notarization; see docs/BUILD.md.

set -euo pipefail

APPHOST="AchievementCaretaker"
publish_dir=""
version=""
arch=""
output_dir="dist"

while [ $# -gt 0 ]; do
    case "$1" in
        --publish-dir) publish_dir="$2"; shift 2 ;;
        --version)     version="$2";     shift 2 ;;
        --arch)        arch="$2";        shift 2 ;;
        --output)      output_dir="$2";  shift 2 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

if [ -z "$publish_dir" ] || [ -z "$version" ] || [ -z "$arch" ]; then
    echo "usage: build-app.sh --publish-dir <dir> --version <x.y.z> --arch <x64|arm64> [--output <dir>]" >&2
    exit 2
fi
case "$arch" in
    x64|arm64) ;;
    *) echo "unknown architecture: $arch" >&2; exit 2 ;;
esac
if [ ! -f "$publish_dir/$APPHOST" ]; then
    echo "no $APPHOST in $publish_dir - was this published for osx-$arch?" >&2
    exit 1
fi
if [ ! -f "$publish_dir/System.Private.CoreLib.dll" ]; then
    echo "$publish_dir looks framework-dependent; publish with --self-contained true" >&2
    exit 1
fi
for tool in codesign ditto; do
    command -v "$tool" >/dev/null 2>&1 || { echo "$tool not found: run this on macOS" >&2; exit 1; }
done

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"

# CFBundleVersion is dotted numbers only: a "~dev" or "-rc" suffix has to come off.
plist_version="${version%%[-+~]*}"

mkdir -p "$output_dir"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
app="$stage/Achievement Caretaker.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

# The whole publish directory is the executable's folder: the runtime finds its
# libraries beside the apphost, as it does on every other platform.
cp -R "$publish_dir/." "$app/Contents/MacOS/"
find "$app/Contents/MacOS" -name '*.pdb' -delete
chmod +x "$app/Contents/MacOS/$APPHOST"

sed "s/@VERSION@/$plist_version/g" "$here/Info.plist.in" > "$app/Contents/Info.plist"

# An .icns from the application's .ico, with what the Mac has. The icon is only cosmetic, so a
# failure is reported, not fatal; an app without one still starts.
make_icon() {
    local ico="$root/AchievementCaretaker.App/Resources/AchievementCaretaker.ico"
    local work iconset
    work="$(mktemp -d)"
    iconset="$work/AchievementCaretaker.iconset"
    mkdir -p "$iconset"
    sips -s format png "$ico" --out "$work/base.png" >/dev/null || return 1
    local size
    for size in 16 32 64 128 256; do
        sips -z "$size" "$size" "$work/base.png" --out "$work/$size.png" >/dev/null || return 1
    done
    cp "$work/16.png"  "$iconset/icon_16x16.png"
    cp "$work/32.png"  "$iconset/icon_16x16@2x.png"
    cp "$work/32.png"  "$iconset/icon_32x32.png"
    cp "$work/64.png"  "$iconset/icon_32x32@2x.png"
    cp "$work/128.png" "$iconset/icon_128x128.png"
    cp "$work/256.png" "$iconset/icon_128x128@2x.png"
    cp "$work/256.png" "$iconset/icon_256x256.png"
    iconutil -c icns "$iconset" -o "$app/Contents/Resources/AchievementCaretaker.icns" || return 1
    rm -rf "$work"
}
if command -v sips >/dev/null 2>&1 && command -v iconutil >/dev/null 2>&1; then
    make_icon || echo "warning: could not build the app icon; continuing without it" >&2
else
    echo "warning: sips or iconutil not found; continuing without an app icon" >&2
fi

# Every nested Mach-O is signed too, which --deep does by walking the bundle.
codesign --force --deep --sign - "$app"
codesign --verify --deep --strict "$app"

outfile="$output_dir/achievement-caretaker-${version}-osx-${arch}.zip"
rm -f "$outfile"
# ditto keeps the bundle's modes and extended attributes, which plain zip does not.
ditto -c -k --keepParent "$app" "$outfile"

echo "built $outfile ($(du -h "$outfile" | cut -f1))"
