#!/usr/bin/env bash
# Server tests on Linux from WSL on a Windows machine (README.md): a copy of the working tree on the Linux disk, then
# `dotnet test` with a filter; with --mounts (or its old name --readonly-mount) also the tests needing mounts (a
# read-only one, a file system on a plain folder), in a user namespace.
#   wsl -d Ubuntu-24.04 -- bash /mnt/c/workspace/SimpleFileExplorer/agents/tools/linux-tests/run.sh [--mounts]
# The filter comes from SFE_TEST_FILTER (WSLENV passes it; a `|` on wsl's command line would be split by the shell).
set -euo pipefail

repo="${SFE_REPO:-/mnt/c/workspace/SimpleFileExplorer}"
copy="${SFE_LINUX_COPY:-$HOME/sfe}"
filter="${SFE_TEST_FILTER:-}"
# No Windows paths: their dotnet would win over the Linux one.
export PATH="$HOME/.dotnet:/usr/local/bin:/usr/bin:/bin"

echo "[linux-tests] copying $repo to $copy"
mkdir -p "$copy"
rsync -a --delete --exclude node_modules --exclude bin --exclude obj --exclude .git --exclude .angular \
    --exclude 'src/Sfe.Api/wwwroot' "$repo/" "$copy/"
cd "$copy"

args=(tests/Sfe.Tests -p:SfeSkipWebClient=true --logger "console;verbosity=normal")
[ -n "$filter" ] && args+=(--filter "$filter")
dotnet test "${args[@]}"

if [ "${1:-}" = "--mounts" ] || [ "${1:-}" = "--readonly-mount" ]; then
    # Mounts need no sudo inside a user namespace (mapped to root there: tests needing a plain user go inconclusive,
    # hence only those of the mounts): a read-only bind mount, and a small tmpfs on a plain folder of a "library".
    source=$(mktemp -d) && mounted=$(mktemp -d) && library=$(mktemp -d)
    mount_tests='Name=AReadOnlyMountIsTold|Name=AFileSystemMountedOnAPlainFolderIsMeasuredOnItself'
    echo "[linux-tests] read-only mount at $mounted, tmpfs at $library/Mounted"
    unshare --user --map-root-user --mount bash -c "
        mount --bind '$source' '$mounted' && mount -o remount,bind,ro '$mounted' &&
        mkdir '$library/Mounted' && mount -t tmpfs -o size=64m tmpfs '$library/Mounted' &&
        export SFE_TEST_READONLY_FOLDER='$mounted' SFE_TEST_MOUNTED_FOLDER='$library/Mounted' &&
        dotnet test tests/Sfe.Tests -p:SfeSkipWebClient=true --no-build --logger 'console;verbosity=normal' \
            --filter '$mount_tests'"
    rmdir "$mounted" "$source" "$library/Mounted" && rmdir "$library"
fi
