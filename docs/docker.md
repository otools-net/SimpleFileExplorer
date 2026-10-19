# Docker

SFE's image runs it as an **External** server ([modes](administration/system.md#installation)): other people open it
from their devices; the library folder and ffmpeg come from the container's environment, never from the web page.
ffmpeg is in the image. It is published at `ghcr.io/otools-net/sfe` for `linux/amd64` and `linux/arm64` (a PC or
server, a NAS, a Raspberry Pi 4/5).

## Run it

```sh
docker run -d --name sfe -p 8080:8080 \
  -v sfe-data:/data \
  -v /srv/media:/library \
  ghcr.io/otools-net/sfe:latest
docker logs sfe        # the setup code, the first time
```

Then open `http://<the host>:8080`, enter the setup code, and create the administrator ([First start](first-start.md)).

The repository's `docker-compose.yml` does the same with `docker compose up -d`; its comments show the optional
settings below.

### Image tags

| Tag | What |
|---|---|
| `latest` | The last release |
| `1.80.0` | That release, never changed: pin it to update only when you choose |
| `1.80` | The last fix of 1.80 |
| `edge` | The work not released yet (each change to the main branch): to try, not for your library |

### Building it yourself

From a checkout of the repository: `docker build -t sfe .`, then `sfe` instead of `ghcr.io/otools-net/sfe:latest`
in the commands (`build: .` in the compose file). For another platform: `docker buildx build --platform linux/arm64 …`.

## Folders

| In the container | What | Notes |
|---|---|---|
| `/data` | Settings, keys, setup code; also the databases, caches and logs unless they go elsewhere (below) | A volume: keep it, or every restart is a new install. SFE's own folder (its content root): `appsettings.json` is made there on the first start, the index is `data/sfe.db` |
| `/library` | The first space's library (`Sfe__Root`) | Mount it read-only (`:ro`) for browsing only; read-write to rename, move, upload, edit and delete from SFE |

### Where the database, the caches and the logs go

Each can have its own folder, given by an environment variable; mount a volume or a host folder there:

| Variable | What goes there | Default |
|---|---|---|
| `SFE_DATA` | Settings, keys, setup code (SFE's content root) | `/data` |
| `SFE_DATABASE_DIR` | The SQLite databases: `sfe.db`, and `tenants/` for the other spaces | `/data/data` |
| `SFE_CACHE_DIR` | Thumbnails, trickplay, video fingerprints, the other spaces' caches | `/data/data/thumbs` |
| `SFE_LOGS_DIR` | The daily log files | `/data/data/logs` |

```sh
docker run -d --name sfe -p 8080:8080 \
  -v sfe-data:/data \
  -v sfe-database:/database -e SFE_DATABASE_DIR=/database \
  -v /mnt/fast-disk/sfe-cache:/cache -e SFE_CACHE_DIR=/cache \
  -v /srv/media:/library \
  sfe
```

- The image has `/database`, `/cache` and `/logs` ready (owned by SFE's user), so a named volume mounted there starts
  writable. Any other path works too if SFE's user can write it; the container stops at once, saying which folder, when
  it can't.
- SFE's own settings win over these variables when given: `ConnectionStrings__Sfe` (e.g. SQL Server, with
  `Sfe__Database__Provider=SqlServer`), `Sfe__Thumbnails__CacheDirectory`, `Sfe__Logs__Directory`.
- **Moving them later:** stop the container and move the files first. A new `SFE_DATABASE_DIR` without `sfe.db` in it
  starts an empty install (accounts, tags and index gone from view, though the old file stays where it was). A new
  `SFE_CACHE_DIR` only costs time: thumbnails and trickplay are made again.

SFE runs as the image's `app` user (uid 1654): it must be able to read the library (and write it, for file
management). On a host folder owned by someone else, give that user access, or run the container as the folder's owner
(`--user 1000:1000`, then make `/data` and the other folders writable for that user too).

## Settings

The image sets:

| Variable | Value |
|---|---|
| `Sfe__Mode` | `External` |
| `Urls` | `http://+:8080` |
| `Sfe__Root` | `/library` |
| `Sfe__Thumbnails__FfmpegPath` | `/usr/bin/ffmpeg` |

Any setting can be given the same way (`-e Sfe__…`, or `environment:` in a compose file; `__` stands for `:` of
[Configuration](configuration.md)), or edited in `/data/appsettings.json` (then restart the container). A variable
always wins over the file. The usual ones:

- `Sfe__Setup__Code`: a setup code of your own (at least 12 characters) instead of the one SFE prints in its log.
- `Sfe__Tenants__AllowedRoots__0`, `__1`…: the folders other [spaces](administration/spaces-and-accounts.md) may use;
  mount them (e.g. `-v /srv/family:/libraries/family`) and allow their parent (`/libraries`). By default only the
  library.
- `Sfe__Security__TrustedProxies__0`: a reverse proxy on another address than the container's own (e.g. the Docker
  network `172.16.0.0/12`), so that sign-in limits see the visitors' addresses
  ([reverse proxy](installation.md#reaching-it-from-outside-https-and-a-reverse-proxy)); with https,
  `Sfe__Security__RequireHttps=true`.
- `ConnectionStrings__Sfe` and `Sfe__Database__Provider=SqlServer`: SQL Server instead of the built-in SQLite database.
- `TZ`: the time zone of schedules and logs (default UTC).

To change the library folder later, change the `/library` mount (or `Sfe__Root`) and restart: the page can't, the
server being External.

## Updating

```sh
docker pull ghcr.io/otools-net/sfe:latest
docker rm -f sfe && docker run … (as above)
```

With the compose file: `docker compose pull && docker compose up -d`. A built image: `git pull`, `docker build -t sfe .`,
then the same. The data volume keeps everything; the first page after an update asks for the setup code to confirm it
([Upgrading](upgrading.md)), and [CHANGELOG.md](../CHANGELOG.md) says what changed.
