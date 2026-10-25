# Testing: rules and infrastructure

Code: `tests/Sfe.Tests/` (`Infrastructure/`: `TestLibrary`, `CoreServices`, `ApiClients`, `WarningLog`); client
`src/Sfe.Web/src/testing/` (`setup.ts`, `fakes.ts`) and `*.spec.ts` next to the code. Gotchas:
[testing-backend.md](testing-backend.md), [testing-client.md](testing-client.md).

## Rules

- **Every new feature comes with tests**, in the same change: backend (MSTest in `tests/Sfe.Tests`) for server
  behaviour, frontend (a `*.spec.ts`) for client behaviour, both when it spans the two.
- A bug fix gets a test that fails without it.
- Not done until `dotnet test` and `npm test` pass.

## Backend

- Each test gets its own `TestLibrary` (temp tree with odd/Unicode names + own SQLite file, setup code, keys folder).
- `CoreServices.CreateAsync` for Core (no accounts: the server level only).
- `WebApplicationFactory<Program>` for HTTP through `ApiClients` (`CreateFactory`, `SetUpAsync` = signed in as the
  server administrator, `SignInAsync`, `Create` = a visitor; all send `X-SFE-Request`). The test server has no network
  address: `CreateFrom(factory, "203.0.113.7")` (header `ApiClients.AddressHeader`, applied by a startup filter before
  the app's middleware) for limits per address and forwarded headers (`127.0.0.1` = a local proxy).
- A test that makes a database first (`CoreServices`, an old settings file + index) and then starts the HTTP server
  meets the update gate: call `ApiClients.MigrateAsync(factory)` before anything else.
- Core has `InternalsVisibleTo Sfe.Tests`.
- Media a test needs (videos, audio, pictures made by ffmpeg) come from `tests/samples/media` through `TestSamples`,
  never rendered by the test ([test-samples.md](test-samples.md)).
- New search cases go in `SearchCombinationTests` (data rows over its fixture).

## Client

- A `*.spec.ts` next to the code; components get `mockApi()` + `testProviders(api)` + `loadConfig(api)` from
  `src/testing/fakes.ts` (`fakeConfig()` = a server administrator with every right; `limitedConfig(rights)` = another
  account; `settingsValues()` = a valid value for every settings field).
- `Api` itself is tested with `HttpTestingController`.
- The administration page: `adminPageProviders(api)` + `RouterTestingHarness` (navigate to `/` and back for a new page
  and form).
