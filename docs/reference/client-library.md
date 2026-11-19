# C# client library

`Sfe.Client` is a .NET package for programs that work with an SFE server: list, read, upload, move, copy and delete
files (in the library and in its [connections](../administration/connections.md)), search the index and tag files, without knowing the [API](api.md)'s routes, headers, upload chunks or job
polling. It targets `netstandard2.0` (.NET Framework 4.6.2 and later, .NET Core, Mono) and `net8.0` and later.

## Installing it from the release

The package is not on nuget.org: it comes with each SFE release, next to the portable packages, as
`Sfe.Client.<version>.nupkg` (and `Sfe.Client.<version>.snupkg`, its symbols for the debugger). Put both in a folder,
then add that folder as a package source and the package to your project:

```sh
dotnet nuget add source C:\Packages\sfe --name sfe-local
dotnet add package Sfe.Client
```

Built from the source, `build/build.ps1` (or `build/build.sh`) leaves it in `artifacts/`; `dotnet pack
src/Sfe.Client -c Release -o <folder>` makes only the package.

## Signing in with a token

The library calls the server with a personal access token: make one on
[My account › Access tokens](../administration/my-account.md#access-tokens) (with only the rights the program needs,
and in one library if it works in one) and keep it out of the program's code, in its settings or an environment
variable. The rules of [calling with a token](api.md#calling-with-a-token) apply: the token does what its account may
do, never more, and the routes refused to tokens stay refused.

```csharp
using Otools.Net.Sfe.Client;

var token = Environment.GetEnvironmentVariable("SFE_TOKEN")!;
using var sfe = new SfeClient(new Uri("http://nas:5080"), token);
```

The address is the server's, with its virtual directory if it has one (`http://nas/sfe/`). More options:

```csharp
using var sfe = new SfeClient(new SfeClientOptions
{
    BaseAddress = new Uri("http://nas:5080"),
    Token = token,
    Library = 2,                                   // the library's id (GET config's tenants); none: the token's
    RequestTimeout = TimeSpan.FromSeconds(30),     // until the answer starts; a download may then take longer
    MaxRetries = 3,                                // 429 and 503 answers are tried again
});
```

In a program with services (ASP.NET Core, a worker), register it once and take `SfeClient` where needed; its
`HttpClient` comes from `IHttpClientFactory`:

```csharp
builder.Services.AddSfeClient(options =>
{
    options.BaseAddress = new Uri(builder.Configuration["Sfe:Address"]!);
    options.Token = builder.Configuration["Sfe:Token"];
});
```

`sfe.Libraries.ListAsync()` lists the libraries the token reaches; `sfe.Server.GetInfoAsync()` tells the server's
version and the caller's rights.

## Examples

Paths are the library's, from its root, with `/` (`Photos/2026/beach.jpg`); `SfePath.Combine`, `Parent` and `Name`
build them.

**List a folder** (pages are fetched as the loop reads them):

```csharp
await foreach (var item in sfe.Files.ListAsync("Photos/2026"))
    Console.WriteLine(item.IsFolder ? $"{item.Name}/" : $"{item.Name} {item.Size:N0} bytes");
```

**Upload** a file in chunks, with progress. A cut-off connection is picked up where it stopped; a failed upload is
kept on the server and the next call with the same file goes on with it. A name already taken is refused unless
`Conflicts` says otherwise (`Replace`, `KeepBoth`, `Skip`):

```csharp
await using var file = File.OpenRead(@"D:\Videos\holiday.mp4");
var progress = new Progress<long>(bytes => Console.Write($"\r{bytes:N0} bytes"));
var options = new SfeUploadOptions { Conflicts = SfeConflictChoice.KeepBoth, Progress = progress };
var uploaded = await sfe.Files.UploadAsync("Videos/holiday.mp4", file, options);
```

`UploadFolderAsync` uploads a folder of the computer with everything in it.

**Download** a file, or part of it (`SfeDownloadOptions.Offset` and `Length`); a cut-off download goes on where it
stopped as long as the file is unchanged. `OpenReadAsync` gives a stream instead:

```csharp
await using var target = File.Create("holiday.mp4");
await sfe.Files.DownloadAsync("Videos/holiday.mp4", target);
```

**Move or copy** files and folders: the server runs a job, which the program follows. A name clash in the target is
answered by a fixed choice, or by a callback asked for each clash:

```csharp
var job = await sfe.Files.MoveAsync(["Inbox/a.jpg", "Inbox/b.jpg"], "Photos/2026");
var done = await job.WaitAsync(new SfeJobWaitOptions
{
    OnConflict = (conflict, _) => Task.FromResult(new SfeConflictAnswer(SfeConflictChoice.KeepBoth, forAll: true)),
});
Console.WriteLine($"{done.State}: {done.Done} of {done.Total}");
```

`CreateFolderAsync`, `RenameAsync`, `DeleteAsync` (to the trash; `sfe.Trash` lists and restores) complete the set;
`sfe.Properties` gives folder totals and checksums, `sfe.Archives.DownloadZipAsync` several items as one zip.

**Search** the index in the [search language](search-syntax.md) of the app, in a folder and an order of your
choice; only indexed files are found ([scanning](../user-guide/scanning.md)):

```csharp
var count = await sfe.Search.CountAsync("e:mp4,mkv d:>30m");
var options = new SfeSearchOptions { Folder = "Videos", Sort = SfeSearchSort.Size, Descending = true };
await foreach (var video in sfe.Search.QueryAsync("e:mp4,mkv d:>30m", options))
    Console.WriteLine($"{video.Path} {video.Duration:N0} s");
```

`GetPageAsync` gives one page and its cursor, for a program that pages by itself.

**Tag** files (a file not indexed yet is indexed on the way):

```csharp
var tags = await sfe.Tags.FindAsync("holiday");
var holiday = tags.Matches.FirstOrDefault()
    ?? await sfe.Tags.CreateAsync(new SfeTagDraft { Name = "Holiday", Color = "teal" });
await sfe.Tags.AddAsync(["Videos/holiday.mp4"], [holiday.Id]);
var fileTags = await sfe.Tags.GetFileTagsAsync("Videos/holiday.mp4");
```

**Connections** (Dropbox…, made on the web page: their sign-in needs a browser) are places of their own beside the
library. `sfe.Connections.ListAsync()` lists those the token may use, with what each one's provider can do;
`sfe.Connections[id].Files` has the file methods of `sfe.Files`, with paths from the connection's root folder:

```csharp
var dropbox = (await sfe.Connections.ListAsync()).First(c => c.Kind == "dropbox");
var files = sfe.Connections[dropbox.Id].Files;
await foreach (var item in files.ListAsync("Camera Uploads"))
    Console.WriteLine(item.Name);
await files.UploadAsync("Backup/notes.txt", File.OpenRead("notes.txt"));
await files.DownloadAsync("Camera Uploads/beach.jpg", File.Create("beach.jpg"));
var moved = await files.MoveAsync(["Camera Uploads/beach.jpg"], "Photos");   // done by the provider, at once
```

Deletes go to the provider's own trash, not SFE's. Moves and copies inside a connection answer a result per item;
between the library and a connection, or two connections, they are jobs like the library's, which `RetryAsync` starts
again for what failed once they ended:

```csharp
var connection = sfe.Connections[dropbox.Id];
var job = await connection.CopyFromLibraryAsync(["Photos/2026"], "Backup");
var done = await job.WaitAsync();
if (done.Results.Any(r => r.Status != SfeItemStatus.Done))
    done = await (await job.RetryAsync()).WaitAsync();
await connection.MoveToLibraryAsync(["Camera Uploads/beach.jpg"], "Inbox");
await connection.CopyToAsync(["Shared"], sfe.Connections[otherId], "From Dropbox");
```

What a provider can't do (a part of a file, uploads, moves or copies on its side: `GetCapabilitiesAsync`) throws
`SfeNotSupportedException` with its `Capability` before anything is sent. A library connection's folder rules are
administration: `sfe.Raw.Api.V1.Connections[id].FolderRules`.

A connection's files are **searched and tagged** in the library's index of it, with the search language and the
library's tags; nothing is asked of the provider. The index is filled by the connection's syncs (the first search
starts the first one), so each page says how much of the connection it holds (`Coverage`: `None`, `Partial`,
`Complete`), and tagging before the first sync has ended throws an `SfeConflictException` whose `ConnectionProblem`
is `NotIndexed`. Who manages the connection reads where its index stands and syncs it now:

```csharp
var connection = sfe.Connections[dropbox.Id];
await connection.SyncIndexAsync();
while ((await connection.GetIndexAsync()) is { Running: true } or { LastSync: null })
    await Task.Delay(1000);
var options = new SfeSearchOptions { Folder = "Camera Uploads", Sort = SfeSearchSort.Modified };
await foreach (var photo in connection.Search.QueryAsync("e:jpg s:>2mb", options))
    Console.WriteLine(photo.Path);
await connection.Tags.AddAsync(["Camera Uploads/beach.jpg"], [holiday.Id]);
var trip = new SfeTagScope { Folder = "Camera Uploads/2026", Recursive = true };
await connection.Tags.ChangeScopeAsync(trip, add: [holiday.Id], remove: []);   // every indexed file of the tree
```

**Everything else** (scans, settings, accounts, logs…) is reached through `sfe.Raw`, a client of every v1 route
generated from the [OpenAPI document](api.md): `await sfe.Raw.Api.V1.Scan.GetAsync()`. Its calls are not retried, and
its errors are Kiota's `ApiException`.

## Errors

A refusal of the server is thrown as an `SfeApiException` with the HTTP `StatusCode`, the
[problem type](api.md#problem-types) word in `ProblemType` (the one to test: `file-exists`, `scan-running`…), a
`Detail` for people, and the fields refused in `Errors` (400). Its subclasses tell the usual cases apart:

| Exception | When |
|---|---|
| `SfeNotFoundException` | 404: not there, or the caller doesn't see it |
| `SfeRefusedException` | 401 (wrong, expired or revoked token) or 403 (a right missing) |
| `SfeConflictException` | 409: a name taken, a job still running… |
| `SfeRateLimitedException` | 429 after the retries (`RetryAfter`) |
| `SfeServerNotReadyException` | the server waits for its setup or update, or is a Local server answering its own computer only |
| `SfeNotSupportedException` | the server doesn't offer that function (older, or turned off), or a connection's provider can't do it (`Capability`) |
| `SfeJobConflictException` | a job waits on a name clash and nothing answers it |

A connection's failures keep these classes and add `ConnectionProblem`, from their `connection-…` word:
`NeedsReconnecting` (409: its manager signs in again on the web page), `RateLimited` (429: the provider asks to wait;
retried as any 429), `Unavailable` (502: the provider can't be reached), `Refused` (403), `NotSupported` (409),
`Unusable` (409: the connection is off), `NotIndexed` (409: the library's index doesn't hold its files yet; try
again once its sync has run).

Answers 429 and 503 are tried again (as told by `Retry-After`, else after a growing delay) when sending the request
twice does no harm: reads, deletes, upload chunks; never a move, a copy or a new folder. Every method takes a
`CancellationToken`; `RequestTimeout` throws a `TimeoutException`.

## Versions

The package has the version of the SFE release it comes with. It works with every server that has API v1 (any SFE
from the version that brought `/api/v1`), older or newer than itself: what a newer server adds (properties, kinds of
items) is ignored or read as `Unknown`, and a function an older server lacks throws `SfeNotSupportedException`
(`sfe.Server.RequireAsync` checks one first). Within a major version the package's own methods only grow; a change
that would break programs built against it is said in the [changelog](../../CHANGELOG.md).
