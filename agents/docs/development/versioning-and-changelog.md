# Versioning and changelog

Files: `Directory.Build.props` (`<Version>`), `CHANGELOG.md`. How the version is read at run time:
[app-version.md](../architecture/app-version.md).

## Branches

- **`edge`**: all work is committed there until it is ready (decided 2026-10-19). Its pushes build nothing on GitHub
  (`docker-image.yml` runs on pushes to `main` and tags only); no version bump there.
- **`main`**: gets `edge` when the user says it is ready, with the version bump in the merge (or a commit right
  before it on `edge`, then fast-forwarded). Never commit work directly on `main`.

## Versioning

`<Version>` is semantic `major.minor.patch`; the build scripts accept nothing else. It changes **only when `edge` is
merged into `main`**, once for everything merged, from the `## Unreleased` section of `CHANGELOG.md`:

- only `### Fixed` → bump patch: `1.0.0` → `1.0.1`;
- any `### Added` / `### Changed` → bump minor, reset patch: `1.0.1` → `1.1.0`;
- major only when the user asks; nothing to merge in the changelog (docs, tests, build scripts, `AGENTS.md`,
  `agents/` only) → no bump.

## Releasing (the Docker image)

- A pushed tag `v<Version>` publishes `ghcr.io/otools-net/sfe:<Version>`, `:<major>.<minor>` and `:latest`
  (`.github/workflows/docker-image.yml`; it refuses a tag differing from `Directory.Build.props`). Tag the commit that
  bumps the version: `git tag v1.80.0`, `git push origin v1.80.0`, only when the user asks (it publishes).
- Each push to `main` publishes the image `:edge` (the last merge, not released yet); the `edge` branch publishes
  nothing. Details: [docker.md](../environment/docker.md), `.github/workflows/docker-image.yml`.

## Changelog

- Each commit on `edge` with a user-visible change adds its lines under `## Unreleased` at the top of `CHANGELOG.md`
  (created when missing): `### Added` / `### Changed` / `### Fixed`, one short user-facing line per item. Not for
  docs, tests or build scripts alone.
- The merge into `main` renames `## Unreleased` to `## x.y.z` (the new version). The parser skips headings that aren't
  versions, so the update page never shows `Unreleased` and `TheShippedChangelogHasTheRunningVersion` holds on `edge` too.
- The file is embedded in Sfe.Api and shown on the update page (`TheShippedChangelogHasTheRunningVersion` checks the
  top section is the running version); items may use `` `code` `` and `**bold**` only.
- What an upgrade does to an install stays in `docs/upgrading.md`.
