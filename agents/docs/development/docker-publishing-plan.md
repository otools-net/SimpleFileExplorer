# Publishing the Docker image on ghcr.io: plan

Code (planned): `.github/workflows/docker-image.yml`, `Dockerfile` (commit build argument), `docs/docker.md`
(published image). What the image is: [docker.md](../environment/docker.md); the epic it came from:
[server-modes-plan.md](server-modes-plan.md).

Goal: each release of SFE is published as `ghcr.io/otools-net/sfe:<version>` (also `:<major>.<minor>` and `:latest`),
for `linux/amd64` and `linux/arm64`, built by GitHub Actions from a version tag; each push to `main` as `:edge`. Nobody
builds images by hand, and `build/build.sh` / `build.ps1` stay as they are: **no Docker there** (decided 2026-10-19).

Status legend: `[ ]` to do, `[~]` started, `[x]` done (version), `[-]` dropped (why).

## How it works

- **GitHub Container Registry (ghcr.io)** stores images under the account that owns the repository: here the user
  account `otools-net`, so `ghcr.io/otools-net/<name>`. Names are lower case.
- **GitHub Actions** runs a workflow file of the repository (`.github/workflows/*.yml`) on GitHub's machines when
  something happens, here: a tag `v1.80.0` is pushed. The workflow builds the `Dockerfile` for both platforms and
  pushes the image.
- **No password to store:** each run gets a temporary `GITHUB_TOKEN`; with `permissions: packages: write` in the
  workflow it may push to the organization's registry. The package is linked to the repository by the image label
  `org.opencontainers.image.source` (the metadata action adds it).
- A new package is **private** until someone with admin rights on it makes it public (once). Private images need a
  `docker login` to pull.

## Decisions

- **Image name `ghcr.io/otools-net/sfe`** (short, like the app), not `…/simplefileexplorer`.
- **Published from version tags** (`v` + the version of `Directory.Build.props`, e.g. `v1.80.0`), **and every push to
  `main` as `edge`** (decided 2026-10-19), plus a manual run button (`workflow_dispatch`, optional extra tag).
- **The image is public** (decided 2026-10-19): users pull without signing in.
- **Tags per release:** `1.80.0`, `1.80`, `latest` (`docker/metadata-action`, semver patterns). A pre-release tag
  (`v1.81.0-beta.1`) gets only its own tag, never `latest`.
- **Tests before publishing:** a first job runs `dotnet test` and `npm test` on Ubuntu (as `build/build.sh` does); the
  image job needs it.
- **The tag must match the version:** the workflow fails when `v<tag>` differs from `<Version>` in
  `Directory.Build.props` (the app shows that version, the update page reads it).

## Verified constraints

- The remote is `git@github-otools:otools-net/SimpleFileExplorer.git` (a public repository of the user `otools-net`);
  no tags yet (2026-10-19).
- `.dockerignore` leaves `.git` out, so the SDK can't append the commit to the version inside the image
  (`AppVersion.Commit` empty): pass it as a build argument (`SFE_COMMIT` → `-p:SourceRevisionId=…`).
- Both build stages run on the build platform (`--platform=$BUILDPLATFORM`): an arm64 image only emulates the last
  stage's `apt-get` (QEMU on the runner), so a two-platform build takes minutes, not an hour.
- The repository's visibility decides the cost: Actions minutes and package storage are free for public repositories;
  a private one uses the organization's included minutes and storage.

## Steps

### 1. The workflow (agent)

- [x] Written (2026-10-19), YAML checked; **not run yet** (needs the push and the settings of step 2). Actions'
      versions as of writing: checkout v5, setup-dotnet v5, setup-node v5, docker/* v3, metadata v5, build-push v6.
- [x] `.github/workflows/docker-image.yml`:
  - `on: push: tags: ['v*']` and `workflow_dispatch`;
  - job `test` (ubuntu-latest): `actions/setup-dotnet` (10.0.x), `actions/setup-node` (24), `apt-get install ffmpeg`,
    `dotnet test`, `npm ci && npm test` in `src/Sfe.Web`; check tag = `Directory.Build.props` version;
  - job `image` (`needs: test`, `permissions: contents: read, packages: write`):
    `docker/setup-qemu-action`, `docker/setup-buildx-action`, `docker/login-action` (registry `ghcr.io`, user
    `${{ github.actor }}`, password `${{ secrets.GITHUB_TOKEN }}`), `docker/metadata-action` (image
    `ghcr.io/otools-net/sfe`, tags `type=semver,pattern={{version}}`, `{{major}}.{{minor}}`, `latest` only for
    releases), `docker/build-push-action` (`platforms: linux/amd64,linux/arm64`, `push: true`, build argument
    `SFE_COMMIT=${{ github.sha }}`, cache `type=gha`);
  - actions pinned to a major version (`@v4`…), checked current when written.
- [x] `Dockerfile`: `ARG SFE_COMMIT` in the server stage, `-p:SourceRevisionId=$SFE_COMMIT` when set; the OCI labels
      come from the metadata action.
- [ ] First run: the push of the commit holding the workflow to `main` runs it (tests, then `:edge`). Read the run;
      a failing Linux test shows there first. The **Run workflow** button with a tag (`test`) is the way to retry
      without a commit.

### 2. GitHub settings (the owner, once: see the steps in the answer of 2026-10-19 / `docs/docker.md` when written)

`otools-net` is a **user** account, not an organization (GitHub API `type: User`, checked 2026-10-19), and the
repository is public: no organization settings, Actions and storage free.

- [ ] Repository › Settings › Actions › General: "Allow all actions and reusable workflows" (the default); "Workflow
      permissions" may stay "Read" (the workflow asks for `packages: write` itself).
- [ ] After the first run: `https://github.com/otools-net?tab=packages` › `sfe` › Package settings › **Change
      visibility** → Public; "Manage Actions access" lists the repository with **Write** (set by the first push).

### 3. Releasing (each version)

- [x] In [versioning-and-changelog.md](versioning-and-changelog.md): tag `v<version>` on the version commit, push the
      tag (only when the user asks: it publishes).
- [ ] The first release tag (`v1.79.0` or the next): `:latest` exists only from then on; until then only `:edge`.

### 4. Docs

- [x] `docs/docker.md`: the published image first (tags table, run, update by pulling), building it yourself second;
      `docker-compose.yml` uses `ghcr.io/otools-net/sfe:latest` (`build: .` as a comment).
- [x] `agents/docs/environment/docker.md`: the workflow, the commit argument.
- [ ] A `CHANGELOG.md` line in the release that first publishes (`### Added`: the image on ghcr.io).

## Open questions

- None. Decided 2026-10-19: public image, `edge` from `main`.

## Found while building

- Work moved to an `edge` branch (2026-10-19): it builds nothing (the workflow has no trigger for it); the image tag
  `edge` still comes from pushes to `main`, i.e. merges not released yet. A manual run can still build the branch.
