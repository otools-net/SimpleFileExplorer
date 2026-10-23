# Docker image

Files: `Dockerfile`, `build/docker/entrypoint.sh`, `.dockerignore`, `docker-compose.yml` (example); user doc
[docs/docker.md](../../../docs/docker.md). Modes: [server-modes.md](../features/server-modes.md).

- Three stages: `node:24-bookworm-slim` runs `npm ci` + `npm run build` (writes `src/Sfe.Api/wwwroot`); the SDK stage
  restores with only the project files first (layer cache), then publishes the **portable** build
  (`-p:SfePortable=true -p:SfeSkipWebClient=true`, RID from `TARGETARCH`: `amd64` → `linux-x64`, `arm64` →
  `linux-arm64`); `runtime-deps:10.0` (Ubuntu, has ICU) + `apt-get install ffmpeg`, user `$APP_UID` (1654).
- Both build stages run on `$BUILDPLATFORM`: an arm64 image only emulates the last stage's `apt-get`.
- Portable build = web root next to the exe, content root `--contentRoot /data` (as `build/linux/AppRun`). The
  configuration is read from the content root: the entry point copies the shipped `appsettings.json` to `/data` on
  the first start (without it the defaults would pick SQL Server). Data paths stay relative: the index is
  `/data/data/sfe.db`.
- Environment: `Sfe__Mode=External`, `Urls=http://+:8080`, `ASPNETCORE_HTTP_PORTS=` (empty: else "Overriding
  HTTP_PORTS" at each start), `Sfe__Root=/library`, `Sfe__Thumbnails__FfmpegPath=/usr/bin/ffmpeg`,
  `Sfe__OpenBrowser=false`. `HostingKind.Docker` comes from the base image's `DOTNET_RUNNING_IN_CONTAINER`.
- **Data folders by variable** (the entry point, not the app): `SFE_DATA` (content root), `SFE_DATABASE_DIR` →
  `ConnectionStrings__Sfe=Data Source=<dir>/sfe.db` (other spaces' databases go beside it, `tenants/`), `SFE_CACHE_DIR`
  → `Sfe__Thumbnails__CacheDirectory` (thumbnails, trickplay, fingerprints, spaces' caches, server work folder),
  `SFE_LOGS_DIR` → `Sfe__Logs__Directory`. Each only when SFE's own variable isn't set. The script checks each folder is
  writable and exits naming it. `/data`, `/database`, `/cache`, `/logs` exist in the image owned by `$APP_UID`: a named
  volume mounted on a path the image lacks is root's (not writable).
- The console box offers no code-free setup there (`LocalVisitor.ExternalByConfiguration`, also used by
  `Program.NewInstallAsync`).
- Never built by `build/build.sh` / `build.ps1` (decided 2026-10-19); published by GitHub Actions
  (`.github/workflows/docker-image.yml`: tests, then `ghcr.io/otools-net/sfe` for both platforms; tags `vX.Y.Z` →
  version, `X.Y`, `latest`; `main` → `edge`; the `edge` branch → nothing; a manual run may add a tag of its own):
  `.github/workflows/docker-image.yml`.
- `SFE_COMMIT` (build argument) → `-p:SourceRevisionId`: the commit shown next to the version, as `.git` isn't in the
  build context.
- **This machine:** Docker Desktop is installed but not started by default ("failed to connect … docker_engine"):
  start `C:\Program Files\Docker\Docker\Docker Desktop.exe` and wait for `docker version` to show a server. The machine
  is short of RAM: when done, `docker desktop stop` then `wsl --shutdown`.
- Smoke test (2026-10-19, 1.79.0): `docker build -t sfe:test .`, run with a volume on `/data` and a folder on
  `/library` (`-p 18080:8080`), code from `docker logs`, `POST api/setup` with it from the host, `api/browse` 200,
  `api/config` `mode: external`, `features.thumbnails: true` (ffmpeg found). With `SFE_DATABASE_DIR=/database` and
  `SFE_CACHE_DIR=/cache` on named volumes: `sfe.db` in `/database`, thumbnails of a PNG and a WebM (made in the
  container with `ffmpeg -f lavfi -i testsrc`) in `/cache`, nothing under `/data/data` but keys and the setup code;
  `SFE_CACHE_DIR=/usr/x` exits 1 naming the folder. arm64: built with `docker buildx build --platform linux/arm64
  --load` (emulated), the binary starts.
