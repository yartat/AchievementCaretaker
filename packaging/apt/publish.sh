#!/usr/bin/env bash
# AchievementCaretaker
# Copyright (c) 2026 Yaroslav V Tatarenko
#
# See LICENSE in the repository root.
#
# Uploads a .deb to the project's APT repository on Cloudsmith, which signs the
# repository with its own key: nothing here handles a GPG key.
#
#   bash packaging/apt/publish.sh dist/achievement-caretaker_2.2.0_amd64.deb
#
# Reads the repository from CLOUDSMITH_OWNER and CLOUDSMITH_REPOSITORY, and the
# API key from CLOUDSMITH_API_KEY (the variable the Cloudsmith CLI itself reads).
# The key is never printed or written anywhere else.
#
# Needs the Cloudsmith CLI on PATH (pip install cloudsmith-cli). The package is
# uploaded for any distribution and version, because it is self-contained: it
# depends on no runtime package and so installs on Debian and Ubuntu alike.

set -euo pipefail

deb="${1:-}"
if [ -z "$deb" ]; then
    echo "usage: publish.sh <file.deb>" >&2
    exit 2
fi
if [ ! -f "$deb" ]; then
    echo "no such file: $deb" >&2
    exit 1
fi

: "${CLOUDSMITH_OWNER:?CLOUDSMITH_OWNER is not set}"
: "${CLOUDSMITH_REPOSITORY:?CLOUDSMITH_REPOSITORY is not set}"
: "${CLOUDSMITH_API_KEY:?CLOUDSMITH_API_KEY is not set}"

# --republish lets a re-run of the same tag replace the file instead of failing
# on "already exists".
cloudsmith push deb \
    "$CLOUDSMITH_OWNER/$CLOUDSMITH_REPOSITORY/any-distro/any-version" \
    "$deb" \
    --republish

echo "published $(basename "$deb") to $CLOUDSMITH_OWNER/$CLOUDSMITH_REPOSITORY"
