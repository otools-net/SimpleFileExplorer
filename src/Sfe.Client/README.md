# Sfe.Client

The C# client of the HTTP API (v1) of **SFE — Simple File Explorer**: list, read, upload, move, copy and delete the
files of an SFE server and of its connections (Dropbox…), search its index and tag files, without knowing routes,
headers, upload chunks or job polling. Targets `netstandard2.0` and `net8.0`.

```csharp
using Otools.Net.Sfe.Client;

using var sfe = new SfeClient(new Uri("http://nas:5080"), "sfe_XXXX_...");

await foreach (var item in sfe.Files.ListAsync("Photos/2026"))
    Console.WriteLine($"{item.Name} {item.Size}");

await using (var file = File.OpenRead("beach.jpg"))
    await sfe.Files.UploadAsync("Photos/2026/beach.jpg", file);

var job = await sfe.Files.MoveAsync(["Photos/2026/beach.jpg"], "Archive");
await job.WaitAsync();

await foreach (var video in sfe.Search.QueryAsync("holiday e:mp4"))
    Console.WriteLine(video.Path);
```

- **Token:** made on the server's page My account › Access tokens (`sfe_…`), sent as `Authorization: Bearer`.
- **Facade:** `Files`, `Search`, `Tags`, `Trash`, `Properties`, `Archives`, `Libraries`, `Server`, `Connections`
  (`sfe.Connections[id].Files`, transfers with the library as jobs); retries of 429 and 503 honouring
  `Retry-After`; refusals as `SfeApiException` (`SfeNotFoundException`, `SfeRefusedException`,
  `SfeConflictException`, `SfeRateLimitedException`…) with the problem's `type` word.
- **Every other route** (administration included): `sfe.Raw`, a client generated from the v1 OpenAPI document.
- **Services:** `services.AddSfeClient(options => { options.BaseAddress = …; options.Token = …; })`.
- **Versions:** the package has the version of the SFE release it comes with, and works with every server that has
  API v1; a function a server lacks throws `SfeNotSupportedException`.

The full guide is `docs/reference/client-library.md` in the SFE repository.
