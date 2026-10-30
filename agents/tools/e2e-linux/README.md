# End-to-end tests on Linux (as CI)

Runs `tests/e2e` from WSL (Ubuntu 24.04, as GitHub's `ubuntu-latest`) the way `.github/workflows/e2e.yml` does:
Playwright's own Chromium (not Edge), Linux WebKit (it plays WebM and MP3: the video and audio specs run on the
iPhone device, they skip on Windows) and Linux fonts (wider than Windows': a row that just fit can overflow).

```sh
# from Windows (PowerShell): everything, or Playwright arguments
wsl -d Ubuntu-24.04 -- bash /mnt/c/workspace/SimpleFileExplorer/agents/tools/e2e-linux/run.sh
wsl -d Ubuntu-24.04 -- bash /mnt/c/workspace/SimpleFileExplorer/agents/tools/e2e-linux/run.sh --project=iphone --retries=0 specs/video.spec.ts
```

- It copies the working tree (no `.git`, `node_modules`, build outputs) to `~/sfe` and runs there: building on `/mnt/c`
  is slow and its outputs would mix with Windows'. Results, traces and the app log in `~/sfe-e2e` (Windows:
  `\\wsl.localhost\Ubuntu-24.04\home\<user>\sfe-e2e`).
- Node 24.22.2 goes to `~/.local/node` (no sudo; Angular's CLI needs 24.15+, Ubuntu's is 18). The .NET 10 SDK must be
  in WSL already (`dotnet --list-sdks`).
- Once, with sudo (the user runs it): the browsers' libraries and fonts, as CI's `--with-deps`, and ffmpeg:
  `cd ~/sfe/tests/e2e && sudo env PATH=$HOME/.local/node/bin:/usr/bin:/bin npx playwright install-deps chromium webkit`,
  `sudo apt-get install -y --no-install-recommends ffmpeg`.
- Timing budgets (`@timing`) of the iPhone device fail here (WebKit paints in software in WSL): CI's machines pass them.
- Shut WSL down after (`wsl --shutdown`): it holds the RAM it took.
