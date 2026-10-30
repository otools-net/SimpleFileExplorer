#!/usr/bin/env bash
# The end-to-end suite on Linux, as CI runs it (Playwright's own Chromium, Linux WebKit and fonts), from WSL on a
# Windows machine: README.md. Arguments go to `npm run e2e` (`--project=iphone --grep "audio"`...).
set -euo pipefail

repo="${SFE_REPO:-/mnt/c/workspace/SimpleFileExplorer}"
copy="${SFE_E2E_COPY:-$HOME/sfe}"
# No Windows paths: their npm and node would win over Linux ones.
export PATH="$HOME/.local/node/bin:/usr/local/bin:/usr/bin:/bin"
export SFE_E2E_WORK="${SFE_E2E_WORK:-$HOME/sfe-e2e}"
export CI=1

# Angular's CLI needs 24.15 or later.
node_version=v24.22.2
if [ "$(node -v 2>/dev/null)" != "$node_version" ]; then
    echo "[e2e-linux] Node $node_version into ~/.local/node"
    rm -rf "$HOME/.local/node" && mkdir -p "$HOME/.local/node"
    curl -fsSL "https://nodejs.org/dist/$node_version/node-$node_version-linux-x64.tar.xz" | tar -xJ -C "$HOME/.local/node" --strip-components=1
fi

# A copy on the Linux disk: building on /mnt/c is slow, and the Windows build outputs must not mix with these.
echo "[e2e-linux] copying $repo to $copy"
mkdir -p "$copy"
rsync -a --delete --exclude node_modules --exclude bin --exclude obj --exclude .git --exclude .angular \
    --exclude 'src/Sfe.Api/wwwroot' "$repo/" "$copy/"

cd "$copy/tests/e2e"
[ -d node_modules ] || npm ci --silent
npx playwright install chromium webkit >/dev/null
npm run e2e -- "$@"
