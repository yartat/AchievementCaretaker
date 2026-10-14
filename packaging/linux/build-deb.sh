#!/usr/bin/env bash
# AchievementCaretaker
# Copyright (c) 2026 Yaroslav V Tatarenko
#
# See LICENSE in the repository root.
#
# Builds a .deb from an already-published linux-x64 directory.
#
#   ./build-deb.sh --publish-dir publish/linux-x64 --version 1.0.0 --output dist
#
# The publish must be SELF-CONTAINED. A framework-dependent build would have to
# declare Depends: dotnet-runtime-10.0, which only exists in Microsoft's own apt
# repository - so `apt install ./achievement-caretaker_*.deb` would fail
# with an unmet dependency on a stock Debian or Ubuntu. Bundling the runtime
# costs about 100 MB and makes the package install with nothing else added.
#
# Needs: dpkg-deb (>= 1.19, for --root-owner-group), gzip, and coreutils.

set -euo pipefail

PACKAGE="achievement-caretaker"
APPHOST="AchievementCaretaker"
publish_dir=""
version=""
output_dir="dist"
maintainer="${DEB_MAINTAINER:-Yaroslav V Tatarenko <yaroslavtatarenko@gmail.com>}"
homepage="${DEB_HOMEPAGE:-https://github.com/yartat/AchievementCaretaker}"

while [ $# -gt 0 ]; do
    case "$1" in
        --publish-dir) publish_dir="$2"; shift 2 ;;
        --version)     version="$2";     shift 2 ;;
        --output)      output_dir="$2";  shift 2 ;;
        --maintainer)  maintainer="$2";  shift 2 ;;
        --homepage)    homepage="$2";    shift 2 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

if [ -z "$publish_dir" ] || [ -z "$version" ]; then
    echo "usage: build-deb.sh --publish-dir <dir> --version <x.y.z> [--output <dir>]" >&2
    exit 2
fi
if [ ! -x "$publish_dir/$APPHOST" ] && [ ! -f "$publish_dir/$APPHOST" ]; then
    echo "no $APPHOST in $publish_dir - was this published for linux-x64?" >&2
    exit 1
fi
if [ ! -f "$publish_dir/libSkiaSharp.so" ]; then
    echo "no libSkiaSharp.so in $publish_dir - the UI would not start" >&2
    exit 1
fi
if [ ! -f "$publish_dir/System.Private.CoreLib.dll" ]; then
    echo "$publish_dir looks framework-dependent; publish with --self-contained true" >&2
    exit 1
fi

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"

stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

install -d -m 0755 \
    "$stage/DEBIAN" \
    "$stage/usr/lib/$PACKAGE" \
    "$stage/usr/bin" \
    "$stage/usr/share/applications" \
    "$stage/usr/share/icons/hicolor/scalable/apps" \
    "$stage/usr/share/doc/$PACKAGE"

cp -a "$publish_dir/." "$stage/usr/lib/$PACKAGE/"

# Debug symbols are most of the payload and nothing needs them at runtime.
find "$stage/usr/lib/$PACKAGE" -name '*.pdb' -delete

# The LTTng trace provider is the one file in a self-contained publish with a
# dependency the package does not satisfy - liblttng-ust.so.0, which Debian does
# not install by default. The runtime dlopens it only when LTTng tracing is
# switched on and carries on without it, so dropping it is cheaper than either
# shipping a library with an unresolvable link or depending on liblttng-ust for
# a feature nobody here uses.
find "$stage/usr/lib/$PACKAGE" -name 'libcoreclrtraceptprovider.so' -delete

# Debian wants shared libraries non-executable; only the apphost is run.
find "$stage/usr/lib/$PACKAGE" -type d -exec chmod 0755 {} +
find "$stage/usr/lib/$PACKAGE" -type f -exec chmod 0644 {} +
chmod 0755 "$stage/usr/lib/$PACKAGE/$APPHOST"

# Worth asserting: a package whose apphost is not executable installs cleanly
# and then does nothing at all. Some filesystems - a Windows working copy, for
# one - silently ignore the chmod above.
if [ ! -x "$stage/usr/lib/$PACKAGE/$APPHOST" ]; then
    echo "could not make $APPHOST executable; build this on a filesystem with POSIX permissions" >&2
    exit 1
fi

# A wrapper rather than a symlink into /usr/lib: the apphost resolves its own
# directory to find the runtime, and keeping /usr/bin free of that logic means
# the launcher stays readable.
cat > "$stage/usr/bin/$PACKAGE" <<EOF
#!/bin/sh
exec /usr/lib/$PACKAGE/$APPHOST "\$@"
EOF
chmod 0755 "$stage/usr/bin/$PACKAGE"

install -m 0644 "$here/$PACKAGE.desktop" "$stage/usr/share/applications/$PACKAGE.desktop"
install -m 0644 "$root/docs/assets/app-icon.svg" \
    "$stage/usr/share/icons/hicolor/scalable/apps/$PACKAGE.svg"

sed "s|@HOMEPAGE@|$homepage|g" "$here/copyright" \
    > "$stage/usr/share/doc/$PACKAGE/copyright"
chmod 0644 "$stage/usr/share/doc/$PACKAGE/copyright"

printf '%s (%s) unstable; urgency=low\n\n  * Achievement Caretaker %s. See %s\n\n -- %s  %s\n' \
    "$PACKAGE" "$version" "$version" "$homepage" "$maintainer" "$(date -R)" \
    | gzip -9n > "$stage/usr/share/doc/$PACKAGE/changelog.gz"
chmod 0644 "$stage/usr/share/doc/$PACKAGE/changelog.gz"

installed_size="$(du -ks "$stage/usr" | cut -f1)"
sed -e "s|@PACKAGE@|$PACKAGE|g" \
    -e "s|@VERSION@|$version|g" \
    -e "s|@MAINTAINER@|$maintainer|g" \
    -e "s|@INSTALLED_SIZE@|$installed_size|g" \
    -e "s|@HOMEPAGE@|$homepage|g" \
    "$here/control.in" > "$stage/DEBIAN/control"
chmod 0644 "$stage/DEBIAN/control"

# Lets `dpkg -V` verify the install later. dpkg-deb does not generate this.
# -t because md5sum defaults to binary mode on some hosts, and the "hash *path"
# it then writes is not the "hash  path" a md5sums file has to contain.
( cd "$stage" && find usr -type f -exec md5sum -t {} + > DEBIAN/md5sums )
chmod 0644 "$stage/DEBIAN/md5sums"

mkdir -p "$output_dir"
deb="$output_dir/${PACKAGE}_${version}_amd64.deb"
# -Zxz rather than the zstd that dpkg-deb on Debian 13 would pick by default:
# a dpkg older than about Debian 12 cannot read a control.tar.zst at all, and
# this package is downloaded rather than installed from an archive, so it should
# open on whatever the user happens to be running.
dpkg-deb -Zxz --build --root-owner-group "$stage" "$deb"

echo
dpkg-deb --info "$deb"
echo "built $deb ($(du -h "$deb" | cut -f1))"
