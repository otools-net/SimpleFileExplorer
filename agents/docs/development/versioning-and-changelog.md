# Versioning and changelog

Files: `Directory.Build.props` (`<Version>`), `CHANGELOG.md`. How the version is read at run time:
[app-version.md](../architecture/app-version.md).

## Versioning

`<Version>` is semantic `major.minor.patch`; the build scripts accept nothing else. Once per delivery, i.e. per commit:
when the version was already bumped by uncommitted work (compare with `git diff HEAD -- Directory.Build.props`), later
requests before the commit keep it (a minor bump covers fixes too). Otherwise, in the same change:

- **bug fix** → bump patch: `1.0.0` → `1.0.1`;
- **feature added or changed** → bump minor, reset patch: `1.0.1` → `1.1.0` (a task doing both bumps minor only);
- major only when the user asks; no bump for changes to docs, tests or build scripts alone (this includes
  `AGENTS.md` and `agents/`).

## Releasing (the Docker image)

- A pushed tag `v<Version>` publishes `ghcr.io/otools-net/sfe:<Version>`, `:<major>.<minor>` and `:latest`
  (`.github/workflows/docker-image.yml`; it refuses a tag differing from `Directory.Build.props`). Tag the commit that
  bumps the version: `git tag v1.80.0`, `git push origin v1.80.0`, only when the user asks (it publishes).
- Each push to `main` publishes `:edge`. Details: [docker-publishing-plan.md](docker-publishing-plan.md).

## Changelog

- Every version bump adds (or extends, when the version was already bumped) its section at the top of `CHANGELOG.md`:
  `## x.y.z`, then `### Added` / `### Changed` / `### Fixed`, one short user-facing line per item.
- The file is embedded in Sfe.Api and shown on the update page (`TheShippedChangelogHasTheRunningVersion` checks the
  top section is the running version); items may use `` `code` `` and `**bold**` only.
- What an upgrade does to an install stays in `docs/upgrading.md`.
