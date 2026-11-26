#!/usr/bin/env bash
# Server tests on Linux from WSL on a Windows machine (README.md): a copy of the working tree on the Linux disk, then
# `dotnet test` with a filter; with --readonly-mount also the tests needing a read-only mount, in a user namespace.
#   wsl -d Ubuntu-24.04 -- bash /mnt/c/workspace/SimpleFileExplorer/agents/tools/linux-tests/run.sh [--readonly-mount]
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

if [ "${1:-}" = "--readonly-mount" ]; then
    # A read-only bind mount needs no sudo inside a user namespace (mapped to root there: tests needing a plain user
    # go inconclusive, hence only those of the mount).
    source=$(mktemp -d) && mounted=$(mktemp -d)
    echo "[linux-tests] read-only mount at $mounted"
    unshare --user --map-root-user --mount bash -c "
        mount --bind '$source' '$mounted' && mount -o remount,bind,ro '$mounted' &&
        SFE_TEST_READONLY_FOLDER='$mounted' dotnet test tests/Sfe.Tests -p:SfeSkipWebClient=true --no-build \
            --logger 'console;verbosity=normal' --filter 'FullyQualifiedName~AReadOnlyMountIsTold'"
    rmdir "$mounted" "$source"
fi
