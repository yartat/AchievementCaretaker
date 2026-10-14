#!/usr/bin/env bash
# AchievementCaretaker
# Copyright (c) 2026 Yaroslav V Tatarenko
#
# See LICENSE in the repository root.
#
# Prints the version to build, as a dotenv line: VERSION=<x.y.z>.
#
# On a tag it is the tag with any leading "v" removed, and the tag must agree
# with the version in the projects and in the window titles - see
# check-version.sh, which this script calls. Off a tag it is the project
# version with a "~dev" suffix, which sorts *before* the release in both dpkg
# and semver order, so a development build can never look newer than a release.
#
# Reads GitLab's and GitHub's tag variables both, so one script and one version
# gate serve both pipelines.

set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/.." && pwd)"

project_version() {
    sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' \
        "$root/AchievementCaretaker.App/AchievementCaretaker.App.csproj" | head -1
}

tag="${CI_COMMIT_TAG:-}"
if [ -z "$tag" ] && [ "${GITHUB_REF_TYPE:-}" = "tag" ]; then
    tag="${GITHUB_REF_NAME:-}"
fi

if [ -n "$tag" ]; then
    version="${tag#v}"
    # Invoked through bash, not executed: the repository is written from
    # Windows, so these files arrive in a Linux checkout as mode 644 and a
    # direct call dies with "Permission denied" and exit 126.
    bash "$here/check-version.sh" "$version" >&2
else
    build="${CI_PIPELINE_IID:-${GITHUB_RUN_NUMBER:-0}}"
    version="$(project_version)~dev${build}"
fi

if [ -z "$version" ]; then
    echo "could not determine a version" >&2
    exit 1
fi

echo "VERSION=$version"
