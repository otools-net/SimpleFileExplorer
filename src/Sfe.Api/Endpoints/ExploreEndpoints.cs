using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Files;
using Sfe.Core.Files.Compression;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

/// <param name="Kind"><c>folder</c>, <c>file</c>, <c>link</c> (never created when extracting) or <c>other</c>.</param>
/// <param name="Packed">0 when the format doesn't tell it per entry (a solid 7z or rar).</param>
/// <param name="Folders">A folder's folders and entries right inside it, and its files in all.</param>
/// <param name="Where">In search results: the path of the folder it is in, its last folders after "…/" when deep.</param>
/// <param name="In">In search results: the id of the folder it is in (to open it).</param>
/// <param name="Media">A file the library calls a picture, video or audio (<c>image</c>, <c>video</c>, <c>audio</c>: <c>Sfe:FileTypes</c>), else null.</param>
public sealed record ArchiveItem(
    string Kind, long Id, string Name, long Size, long Packed, DateTime? Modified, bool Encrypted,
    long? Folders = null, long? Files = null, long? AllFiles = null, string? Where = null, string? Media = null, long? In = null);

/// <param name="Items">Folders, then files, whose name holds the text; <c>Where</c> is the path of the folder each is in.</param>
/// <param name="Next">Where to go on (a page cut short by its time has fewer items); null once all was looked through.</param>
public sealed record ArchiveSearch(IReadOnlyList<ArchiveItem> Items, string? Next);

public sealed record ArchiveCrumb(long Id, string Name);

/// <param name="Skipped">Folders between the root and <paramref name="Last"/> (deep trees: the way is shown collapsed).</param>
public sealed record ArchiveTrail(int Skipped, IReadOnlyList<ArchiveCrumb> Last);

/// <param name="Complete">False when the list stopped (a limit, damage): <paramref name="Stopped"/> says why.</param>
/// <param name="NeedsPassword">The archive's list is encrypted and no unlock was given (or it expired): ask for it.</param>
/// <param name="Protected">Some of its files are encrypted: opening them needs the password.</param>
/// <param name="Unlocked">The unlock given opens this archive (its list was read with the password, or its files open).</param>
/// <param name="Folder">The folder listed: its totals (<c>AllFiles</c>, <c>Size</c>: its whole tree).</param>
/// <param name="Next">The cursor of the next page; null on the last one.</param>
/// <param name="Path">The folder's whole path in the archive ("" at its top): on a first page, for a folder at most
/// <see cref="ExploreEndpoints.MaxPathDepth"/> deep (links between its files are resolved from it); else null.</param>
public sealed record ArchiveListing(
    string Format, bool Complete, string? Stopped, bool NeedsPassword, long Entries, long LeftOut,
    ArchiveItem Folder, ArchiveTrail Trail, IReadOnlyList<ArchiveItem> Items, string? Next, bool Protected = false, bool Unlocked = false,
    string? Path = null);

public sealed record ArchiveUnlockRequest(string? Path, string? Password);

/// <param name="Unlock">Sent back with later requests about this archive (header <c>X-SFE-Archive-Unlock</c>, or
/// <c>unlock</c> in an entry's address); 15 minutes unused, a sign-out or a change of the archive end it.</param>
public sealed record ArchiveUnlocked(string Unlock);

/// <param name="Read">Entries read so far.</param>
public sealed record ArchiveReading(long Read);

/// <param name="Archive">The archive's path in the library.</param>
/// <param name="Target">The folder to extract into.</param>
/// <param name="NewFolder">Into a new folder named after the archive (or after its one top folder); else straight into <paramref name="Target"/>.</param>
/// <param name="Folders">Folders of the archive to extract (ids of its listing), with what they hold; none with <paramref name="Files"/>: all of it.</param>
/// <param name="Files">Files of the archive to extract (ids of its listing).</param>
/// <param name="Unlock">The unlock of the archive (from <c>explore/unlock</c>), or its <paramref name="Password"/>.</param>
public sealed record ExtractRequest(
    string? Archive, string? Target, bool NewFolder = true, IReadOnlyList<long>? Folders = null, IReadOnlyList<long>? Files = null,
    ConflictChoice Conflicts = ConflictChoice.Ask, string? Unlock = null, string? Password = null);

/// <summary>
/// Archives explored without being unpacked (agents/docs/features/archive-reading.md): a folder of the archive at a
/// time, in pages, from its catalog; one entry's data, for the viewer or to download. The archive's content is the
/// file's content: listing needs <c>files.view</c> at it, opening an entry also <c>files.download</c>.
/// </summary>
public static class ExploreEndpoints
{
    public const string NotExplorableType = "not-explorable";
    public const string UnreadableType = "archive-unreadable";
    public const string TooLargeType = "too-large";
    public const string WrongPasswordType = "wrong-password";
    public const string NotProtectedType = "not-protected";
    public const string UnlockHeader = "X-SFE-Archive-Unlock";

    /// <summary>The most of an entry read to try a password: its checks are at its start (zip) or come with its data.</summary>
    private const int TryBytes = 16 * 1024 * 1024;
    public const int PageSize = 200;

    /// <summary>Folders deep at most for a listing to give its path (one lookup per level).</summary>
    public const int MaxPathDepth = 64;
    public const int MaxPageSize = 1000;
    public const string ReadingType = "archive-reading";

    /// <summary>Folders and files of an archive chosen at most for one extraction.</summary>
    public const int MaxExtractChosen = 10_000;

    public static RouteGroupBuilder MapExploreEndpoints(this RouteGroupBuilder api)
    {
        var explore = api.MapGroup("/files/explore").WithTags("Files");

        explore.MapGet("", List)
            .RequireRight(Rights.ViewFiles)
            .RequireRateLimiting(RequestLimits.Media)
            .FolderScoped("files.view at the archive")
            .WithName("ExploreArchive")
            .WithSummary("A folder of an archive (zip, 7z, rar, tar), in pages: its folders first, then its files, by name.")
            .WithDescription($"""
                `folder` is a folder's id (from a previous answer; the root without it) or `at` its path inside the archive.
                The first question about an archive reads its list once, in the background: 202 with the entries read so
                far until it is ready (ask again). `after` is the previous page's `next`; `limit` at most {MaxPageSize}
                (default {PageSize}). An archive not read yet starts its read, {CatalogBuildLimits.PerMinute} a minute per
                caller at most (429 past it). 409 (`{NotExplorableType}`) for a file that isn't an archive SFE explores (one file
                compressed alone, a compressed tar over `Files:Archives:MaxCompressedTarMegabytes`, a name its bytes deny);
                422 (`{UnreadableType}`) when it can't be read now. `complete` false: the list stopped (`stopped` says why).
                """);
        explore.MapGet("/search", Search)
            .RequireRight(Rights.ViewFiles)
            .RequireRateLimiting(RequestLimits.Search)
            .FolderScoped("files.view at the archive")
            .WithName("SearchArchive")
            .WithSummary("Folders and files of an archive, or of one of its folders, whose name holds a text (any case).")
            .WithDescription($"""
                `q` (at least {MinSearchLength} characters) is looked for in the names of the whole archive, the catalog
                looked through for {SearchBudgetMilliseconds} ms at most per request: a page may hold fewer than `limit`
                (default {SearchPageSize}, at most {MaxPageSize}) and still have a `next` (ask again with `after`). Each
                item has `where`, the path of its folder. With `folder` (a folder's id), only what is right inside that folder,
                in its order (folders first, by name), without `where`. 202 while the list is read, as for `GET explore`.
                """);
        explore.MapGet("/entry/{archive}/{id:long}/{name}", GetEntry)
            .RequireRight(Rights.Download)
            .RequireRateLimiting(RequestLimits.Media)
            .FolderScoped("files.view and files.download at the archive")
            .WithName("GetArchiveEntry")
            .WithSummary("One file inside an archive, decompressed as it is read: for the viewer, or downloaded.")
            .WithDescription($"""
                `archive` is the archive's path, UTF-8 in base64url; `id` the entry's id in its listing; `name` its name
                (the browser's file name). Sandboxed as the library's files are, inline only for what the browser shows
                safely (pictures, audio, video, plain text), downloaded otherwise or with `download=true`. Larger than
                `Files:Archives:MaxPreviewMegabytes` only with `download=true` (413 `{TooLargeType}` otherwise). An entry
                claiming more than `Files:Archives:MaxCompressionRatio` times its packed size is refused (422); one that
                would unpack more than `Files:Archives:MaxSkippedMegabytes` of the files before it (a solid 7z or rar, a
                compressed tar) too (413 `{TooLargeType}`: extract it). Its data is
                held to the size the archive declares: more or less is a broken archive, and the download is cut off.
                `unlock` (from `unlock`) opens an encrypted entry, or any of an archive whose list is encrypted.
                """);
        explore.MapGet("/entry/{archive}/at/{**at}", GetEntryAt)
            .RequireRight(Rights.Download)
            .RequireRateLimiting(RequestLimits.Media)
            .FolderScoped("files.view and files.download at the archive")
            .WithName("GetArchiveEntryAt")
            .WithSummary("One file inside an archive by its path in the archive, as `entry/{archive}/{id}/{name}` serves it.")
            .WithDescription("""
                `at` is the file's path inside the archive (folders separated by `/`; as written, else in any case): the
                address the links and pictures of a document of the archive lead to. The same rights, checks and answers
                as by id.
                """);
        explore.MapGet("/text", GetText)
            .RequireRight(Rights.Download)
            .RequireRateLimiting(RequestLimits.Media)
            .FolderScoped("files.view and files.download at the archive")
            .WithName("GetArchiveEntryText")
            .WithSummary("A text file inside an archive, decoded as `GET api/files/text` decodes a library file; read only.")
            .WithDescription($"""
                `id` is the entry's id in the archive's listing (or `at` its path in the archive); `encoding` reads it in that encoding rather than the one it
                seems to have. Up to {TextEndpoints.MaxBytes / (1024 * 1024)} MB (413 `{TooLargeType}`: download it); 415
                `{TextEndpoints.NotTextType}` when it isn't text. `access` allows nothing: an entry is never saved over.
                The same checks as `GET explore/entry` (password, compression ratio, files before it in a solid block).
                """);
        api.MapPost("/files/jobs/extract", Extract)
            .RequireRight(Rights.ExtractFiles)
            .RequireRateLimiting(RequestLimits.Unlock)
            .FolderScoped("files.view and files.download at the archive, files.extract at the target (checked again as the job runs); replacing: the right to delete there")
            .WithTags("Files")
            .WithName("ExtractArchive")
            .WithSummary("Extracts an archive (zip, 7z, rar, tar, gz…), all of it or the entries chosen, in the background.")
            .WithDescription($"""
                Starts an extraction job and answers its first state (202), followed as moves and copies are (`GET
                api/files/jobs/:id`, `answer`, `cancel`). `newFolder` (default): into a new folder named after the archive
                (after its one top folder when it has one), never mixed with what is there; else straight into `target`,
                where name clashes are asked as `conflicts` says. `folders` and `files` (at most {MaxExtractChosen}, ids of
                the archive's listing: its list must have been read, else 409 `{ReadingType}`) choose what is extracted,
                each from its own name. Names leading out of the folder, links, names the system refuses, and files
                claiming more than `Files:Archives:MaxCompressionRatio` times their packed size are left out and listed in
                the result. A protected archive needs `unlock` (or `password`): a wrong one fails the job before anything
                stays. Cancelled or failed, nothing half written stays. At most {RequestLimits.UnlocksPerMinute} a minute
                per caller.
                """);
        explore.MapPost("/unlock", Unlock)
            .RequireRight(Rights.ViewFiles)
            .RequireRateLimiting(RequestLimits.Unlock)
            .FolderScoped("files.view at the archive")
            .WithName("UnlockArchive")
            .WithSummary("Tries a password on an archive; answers an unlock for the requests that need it.")
            .WithDescription($"""
                Checked on the archive itself: its encrypted list (7z, rar), else its smallest encrypted file read through
                (its checks and authentication). 422 (`{WrongPasswordType}`) for a wrong one, 409 (`{NotProtectedType}`)
                for an archive that has nothing encrypted, 202 while its list is still being read. At most
                {RequestLimits.UnlocksPerMinute} a minute per caller (429). The password stays in the server's memory only,
                behind the unlock: never logged, never answered; the unlock serves its caller only, for this archive as it
                is, until 15 minutes unused or a sign-out.
                """);
        return api;
    }

    private static async Task<Results<Ok<ArchiveListing>, Accepted<ArchiveReading>, NotFound, ForbidHttpResult, ProblemHttpResult>> List(
        ArchiveCatalogs catalogs, IOptionsMonitor<SfeOptions> options, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work,
        ArchiveUnlocks unlocks, CatalogBuildLimits limits, MediaClassifier media, CurrentAccess current, HttpContext context, string? path, long? folder, string? at, string? after, int? limit,
        [FromHeader(Name = UnlockHeader)] string? unlock)
    {
        var (full, canonical, refused) = await ResolveAsync(paths, serverFiles, work, current, path, Rights.ViewFiles);
        if (refused is { } forbidden)
            return forbidden ? TypedResults.Forbid() : TypedResults.NotFound();
        if (FormatOf(full, options.CurrentValue) is not { } format)
            return NotExplorable();

        context.Response.Headers.CacheControl = "no-store";
        var password = unlocks.PasswordFor(unlock, (await current.GetAsync()).TenantId, ArchiveUnlocks.OwnerOf(context), full);
        var state = CatalogOf(catalogs, full, canonical, format, unlock, password, limits.For(context));
        if (state.Status == ArchiveCatalogStatus.Building)
            return TypedResults.Accepted((string?)null, new ArchiveReading(state.Read));
        if (state.Status == ArchiveCatalogStatus.Limited)
            return TooManyReads();
        if (state.Catalog is not { } catalog)
            return TypedResults.Problem(state.Error, statusCode: StatusCodes.Status422UnprocessableEntity, type: UnreadableType);

        var id = folder ?? ArchiveCatalog.RootId;
        if (at is not null)
        {
            if (catalog.FindFolder(at) is not { } found)
                return TypedResults.NotFound();
            id = found;
        }
        if (catalog.Folder(id) is not { } shown)
            return TypedResults.NotFound();
        if (after is not null && ParseCursor(after) is null)
            return TypedResults.Problem("`after` is not a cursor of this listing.", statusCode: StatusCodes.Status400BadRequest);

        var summary = catalog.Summary();
        var page = catalog.Children(id, ParseCursor(after), Math.Clamp(limit ?? PageSize, 1, MaxPageSize));
        var trail = catalog.Trail(id, 4)!;
        var items = page.Folders.Select(ToItem).Concat(page.Files.Select(file => ToItem(file, media))).ToList();
        var encryptedFiles = !catalog.InMemory && catalog.SmallestEncrypted() is not null;
        return TypedResults.Ok(new ArchiveListing(
            FormatName(format), summary.Complete, summary.Stopped, summary.NeedsPassword, summary.Entries, summary.LeftOut,
            ToItem(shown), new ArchiveTrail(trail.Skipped, [.. trail.Last.Select(crumb => new ArchiveCrumb(crumb.Id, crumb.Name))]),
            items, page.Next is { } next ? Cursor(next) : null,
            Protected: catalog.InMemory || encryptedFiles, Unlocked: password is not null,
            Path: after is null && shown.Depth <= MaxPathDepth ? catalog.PathOf(id) : null));
    }

    public const int MinSearchLength = 2;
    public const int SearchPageSize = 100;
    public const int SearchBudgetMilliseconds = 1000;

    private static async Task<Results<Ok<ArchiveSearch>, Accepted<ArchiveReading>, NotFound, ForbidHttpResult, ProblemHttpResult>> Search(
        ArchiveCatalogs catalogs, IOptionsMonitor<SfeOptions> options, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work,
        ArchiveUnlocks unlocks, CatalogBuildLimits limits, MediaClassifier media, CurrentAccess current, HttpContext context, string? path, string? q, long? folder, string? after, int? limit,
        [FromHeader(Name = UnlockHeader)] string? unlock)
    {
        var text = q?.Trim() ?? "";
        if (text.Length < MinSearchLength || text.Length > 256)
            return TypedResults.Problem($"Search with {MinSearchLength} to 256 characters.", statusCode: StatusCodes.Status400BadRequest);
        ArchiveSearchCursor? cursor = null;
        ArchiveCursor? inFolder = null;
        if (after is not null && (folder is null ? (cursor = ParseSearchCursor(after)) is null : (inFolder = ParseCursor(after)) is null))
            return TypedResults.Problem("`after` is not a cursor of this search.", statusCode: StatusCodes.Status400BadRequest);
        var (full, canonical, refused) = await ResolveAsync(paths, serverFiles, work, current, path, Rights.ViewFiles);
        if (refused is { } forbidden)
            return forbidden ? TypedResults.Forbid() : TypedResults.NotFound();
        if (FormatOf(full, options.CurrentValue) is not { } format)
            return NotExplorable();

        context.Response.Headers.CacheControl = "no-store";
        var password = unlocks.PasswordFor(unlock, (await current.GetAsync()).TenantId, ArchiveUnlocks.OwnerOf(context), full);
        var state = CatalogOf(catalogs, full, canonical, format, unlock, password, limits.For(context));
        if (state.Status == ArchiveCatalogStatus.Building)
            return TypedResults.Accepted((string?)null, new ArchiveReading(state.Read));
        if (state.Status == ArchiveCatalogStatus.Limited)
            return TooManyReads();
        if (state.Catalog is not { } catalog)
            return TypedResults.Problem(state.Error, statusCode: StatusCodes.Status422UnprocessableEntity, type: UnreadableType);

        var size = Math.Clamp(limit ?? SearchPageSize, 1, MaxPageSize);
        var budget = TimeSpan.FromMilliseconds(SearchBudgetMilliseconds);
        if (folder is { } id)
        {
            if (catalog.Folder(id) is null)
                return TypedResults.NotFound();
            var here = catalog.FindIn(id, text, inFolder, size, budget);
            return TypedResults.Ok(new ArchiveSearch(
                [.. here.Folders.Select(ToItem), .. here.Files.Select(file => ToItem(file, media))], here.Next is { } more ? Cursor(more) : null));
        }
        var page = catalog.Search(text, cursor, size, budget);
        var items = page.Folders.Select(found => ToItem(found.Folder) with { Where = found.Where, In = found.Folder.ParentId })
            .Concat(page.Files.Select(found => ToItem(found.File, media) with { Where = found.Where, In = found.File.FolderId }))
            .ToList();
        return TypedResults.Ok(new ArchiveSearch(items, page.Next is { } next ? $"{(next.InFiles ? 'f' : 'd')}{next.Id}" : null));
    }

    internal static ArchiveSearchCursor? ParseSearchCursor(string token) =>
        token.Length is >= 2 and <= 24 && token[0] is 'd' or 'f' && long.TryParse(token.AsSpan(1), System.Globalization.NumberStyles.None, null, out var id)
            ? new ArchiveSearchCursor(token[0] == 'f', id)
            : null;

    private static ProblemHttpResult TooManyReads() =>
        TypedResults.Problem($"You opened many archives not read before in a short time ({CatalogBuildLimits.PerMinute} a minute): wait a minute.",
            statusCode: StatusCodes.Status429TooManyRequests);

    /// <summary>
    /// The catalog to answer from: the archive's own (on disk); for an archive whose list is encrypted, the one read with
    /// the unlock's password, in memory.
    /// </summary>
    private static ArchiveCatalogState CatalogOf(ArchiveCatalogs catalogs, string full, string canonical, ArchiveFormat format, string? unlock, string? password,
        Func<bool> mayStart)
    {
        var state = catalogs.Get(full, canonical, format, mayStart);
        if (state.Catalog is { } catalog && password is not null && catalog.Summary().NeedsPassword)
            return catalogs.GetInMemory(full, canonical, format, unlock!, password);
        return state;
    }

    private static async Task<Results<Accepted<FileJobView>, NotFound, ForbidHttpResult, ValidationProblem, ProblemHttpResult>> Extract(
        FileJobs jobs, ArchiveCatalogs catalogs, IOptionsMonitor<SfeOptions> options, AccessService accounts, RightsEvaluator rights, LibraryPaths paths,
        ServerFiles serverFiles, WorkFolder work, ArchiveUnlocks unlocks, CatalogBuildLimits limits, CurrentAccess current, HttpContext context, ExtractRequest request)
    {
        var state = await current.GetAsync();
        if (state.Tenant is not { } tenant || !state.Has(Rights.ExtractFiles))
            return TypedResults.Forbid();
        // "" is the library's root.
        if (request.Target is null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["target"] = ["Name the folder to extract into."] });
        var chosen = (request.Folders?.Count ?? 0) + (request.Files?.Count ?? 0);
        if (chosen > MaxExtractChosen)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["folders"] = [$"Choose at most {MaxExtractChosen} folders and files: or the folder holding them."] });
        var (full, canonical, refused) = await ResolveAsync(paths, serverFiles, work, current, request.Archive, Rights.ViewFiles, Rights.Download);
        if (refused is { } forbidden)
            return forbidden ? TypedResults.Forbid() : TypedResults.NotFound();

        var password = unlocks.PasswordFor(request.Unlock, tenant.Id, ArchiveUnlocks.OwnerOf(context), full)
            ?? (string.IsNullOrEmpty(request.Password) ? null : request.Password);
        IReadOnlyList<string>? entries = null;
        if (chosen > 0)
        {
            // Ids are those of the listing the person saw: its catalog says which paths they are.
            if (FormatOf(full, options.CurrentValue) is not { } format)
                return NotExplorable();
            var catalogState = CatalogOf(catalogs, full, canonical, format, request.Unlock, password, limits.For(context));
            if (catalogState.Status == ArchiveCatalogStatus.Building)
                return TypedResults.Problem("The archive's list is still being read: try again in a moment.", statusCode: StatusCodes.Status409Conflict, type: ReadingType);
            if (catalogState.Status == ArchiveCatalogStatus.Limited)
                return TooManyReads();
            if (catalogState.Catalog is not { } catalog)
                return TypedResults.Problem(catalogState.Error, statusCode: StatusCodes.Status422UnprocessableEntity, type: UnreadableType);
            if (catalog.Summary().NeedsPassword)
                return TypedResults.Problem("The archive's list is protected by a password: unlock it first.", statusCode: StatusCodes.Status403Forbidden, type: WrongPasswordType);
            var folderPaths = new Dictionary<long, string?>();
            string? FolderPath(long id) => folderPaths.TryGetValue(id, out var known) ? known : folderPaths[id] = catalog.PathOf(id);
            var list = new List<string>();
            foreach (var id in request.Folders ?? [])
            {
                if (FolderPath(id) is not { } folder)
                    return TypedResults.NotFound();
                if (folder.Length == 0)
                {
                    // The root: all of it.
                    list = null;
                    break;
                }
                list.Add(folder);
            }
            foreach (var id in list is null ? [] : request.Files ?? [])
            {
                if (catalog.Entry(id) is not { } file || FolderPath(file.FolderId) is not { } folder)
                    return TypedResults.NotFound();
                list!.Add(folder.Length == 0 ? file.Name : $"{folder}/{file.Name}");
            }
            entries = list;
        }

        // Every right is checked again as the job runs, with the person's access then.
        var access = new FileJobAccess(accounts, rights, paths, serverFiles, work, tenant, state.User?.Id);
        var view = jobs.Start(new FileJobRequest(FileJobKind.Extract, [canonical], request.Target, request.Conflicts, Entries: entries, NewFolder: request.NewFolder),
            FileEndpoints.Actor(state), access, password);
        return TypedResults.Accepted($"api/files/jobs/{view.Id}", view);
    }

    private static async Task<Results<Ok<ArchiveUnlocked>, Accepted<ArchiveReading>, NotFound, ForbidHttpResult, ProblemHttpResult>> Unlock(
        ArchiveCatalogs catalogs, IOptionsMonitor<SfeOptions> options, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work,
        ArchiveUnlocks unlocks, CatalogBuildLimits limits, CurrentAccess current, HttpContext context, ILoggerFactory loggers, ArchiveUnlockRequest request,
        CancellationToken cancellationToken)
    {
        var (full, canonical, refused) = await ResolveAsync(paths, serverFiles, work, current, request.Path, Rights.ViewFiles);
        if (refused is { } forbidden)
            return forbidden ? TypedResults.Forbid() : TypedResults.NotFound();
        if (FormatOf(full, options.CurrentValue) is not { } format)
            return NotExplorable();
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 1024)
            return TypedResults.Problem("Enter the archive's password.", statusCode: StatusCodes.Status400BadRequest);
        var state = catalogs.Get(full, canonical, format, limits.For(context));
        if (state.Status == ArchiveCatalogStatus.Building)
            return TypedResults.Accepted((string?)null, new ArchiveReading(state.Read));
        if (state.Status == ArchiveCatalogStatus.Limited)
            return TooManyReads();
        if (state.Catalog is not { } catalog)
            return TypedResults.Problem(state.Error, statusCode: StatusCodes.Status422UnprocessableEntity, type: UnreadableType);

        var log = loggers.CreateLogger("Sfe.Api.Archives");
        var opens = await Task.Run(() => Opens(full, format, catalog, request.Password, options.CurrentValue.Files.Archives), cancellationToken);
        if (opens is null)
            return TypedResults.Problem("Nothing in this archive is protected by a password.", statusCode: StatusCodes.Status409Conflict, type: NotProtectedType);
        var access = await current.GetAsync();
        if (opens is false)
        {
            // Never the password: who tried, on what.
            log.LogWarning("Wrong password for the archive {Archive} by {User} from {Address}.", canonical,
                access.User?.UserName ?? "a visitor", context.Connection.RemoteIpAddress);
            return TypedResults.Problem("That password doesn't open this archive.", statusCode: StatusCodes.Status422UnprocessableEntity, type: WrongPasswordType);
        }
        log.LogInformation("Archive {Archive} unlocked by {User}.", canonical, access.User?.UserName ?? "a visitor");
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(new ArchiveUnlocked(unlocks.Add(access.TenantId, ArchiveUnlocks.OwnerOf(context), full, request.Password)));
    }

    /// <summary>
    /// Whether <paramref name="password"/> opens the archive: its encrypted list read (its first entry), else its smallest
    /// encrypted file read through (up to <see cref="TryBytes"/>: the checks of zip and RAR come first, AES' at the end);
    /// null when nothing in it is encrypted.
    /// </summary>
    private static bool? Opens(string full, ArchiveFormat format, ArchiveCatalog catalog, string password, ArchiveOptions settings)
    {
        var maxList = settings.MaxSevenZipListMegabytes * 1024L * 1024;
        try
        {
            if (catalog.Summary().NeedsPassword)
            {
                // The list decrypts (its first entry read) or the reader says the password is wrong.
                _ = ArchiveReader.ReadEntries(full, format, password: password, maxSevenZipList: maxList).Take(1).ToList();
                return true;
            }
            if (catalog.SmallestEncrypted() is not { } file)
                return null;
            using var data = ArchiveReader.OpenEntry(full, format, file.Entry, password, settings.MaxSkippedMegabytes * 1024L * 1024, maxList);
            var buffer = new byte[64 * 1024];
            for (long read = 0, got; read < TryBytes && (got = data.Read(buffer)) > 0; read += got)
            {
            }
            return true;
        }
        catch (ArchiveException error) when (error.NeedsPassword)
        {
            return false;
        }
        catch (ArchiveException error) when (error.TooFar)
        {
            // Too costly to try (deep in a solid block): taken unchecked; a wrong one fails when a file is opened.
            return true;
        }
        catch (ArchiveException)
        {
            // Garbage after the checks (a wrong ZipCrypto password passing its 1-byte check): not opened.
            return false;
        }
    }

    private static async Task<Results<FileStreamHttpResult, NotFound, ForbidHttpResult, ProblemHttpResult>> GetEntry(
        ArchiveCatalogs catalogs, IOptionsMonitor<SfeOptions> options, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work,
        ArchiveUnlocks unlocks, CurrentAccess current, HttpContext context, string archive, long id, string name, bool? download, string? unlock)
    {
        var opened = await OpenEntryAsync(catalogs, options.CurrentValue, paths, serverFiles, work, unlocks, current, context,
            EpubEntries.PathOf(archive), new EntryKey(id, name, null), download == true ? null : options.CurrentValue.Files.Archives.MaxPreviewMegabytes * 1024L * 1024, unlock,
            stored: true);
        return Serve(context, opened, download);
    }

    private static async Task<Results<FileStreamHttpResult, NotFound, ForbidHttpResult, ProblemHttpResult>> GetEntryAt(
        ArchiveCatalogs catalogs, IOptionsMonitor<SfeOptions> options, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work,
        ArchiveUnlocks unlocks, CurrentAccess current, HttpContext context, string archive, string at, bool? download, string? unlock)
    {
        var opened = await OpenEntryAsync(catalogs, options.CurrentValue, paths, serverFiles, work, unlocks, current, context,
            EpubEntries.PathOf(archive), new EntryKey(null, null, at), download == true ? null : options.CurrentValue.Files.Archives.MaxPreviewMegabytes * 1024L * 1024, unlock,
            stored: true);
        return Serve(context, opened, download);
    }

    private static Results<FileStreamHttpResult, NotFound, ForbidHttpResult, ProblemHttpResult> Serve(HttpContext context, OpenedEntry opened, bool? download)
    {
        if (opened.Refused is { } refused)
            return refused;
        var file = opened.File!;
        var contentType = LibraryEndpoints.ContentTypeOf(file.Name);
        var inline = download != true && LibraryEndpoints.OpensInline(contentType);
        var info = new FileInfo(opened.Full);
        context.Response.Headers[HeaderNames.ContentSecurityPolicy] = SecurityHeaders.FilePolicy;
        context.Response.Headers[HeaderNames.CacheControl] = "private, no-cache";
        // A stored zip entry is a slice of the archive: Range is answered (a video seeks in it).
        return TypedResults.Stream(opened.Data!, contentType, inline ? null : file.Name, lastModified: info.LastWriteTimeUtc,
            entityTag: new EntityTagHeaderValue($"\"{EntryTag(opened.Canonical, info, file.Id)}\""), enableRangeProcessing: opened.Data!.CanSeek);
    }

    private static async Task<Results<Ok<TextFile>, NotFound, ForbidHttpResult, ProblemHttpResult>> GetText(
        ArchiveCatalogs catalogs, IOptionsMonitor<SfeOptions> options, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work,
        ArchiveUnlocks unlocks, CurrentAccess current, HttpContext context, string? path, long? id, string? at, string? encoding,
        [FromHeader(Name = UnlockHeader)] string? unlock, CancellationToken cancellationToken)
    {
        if (encoding is not null && !TextCodec.ReadEncodings.Contains(encoding))
            return TypedResults.Problem($"`encoding` is one of {string.Join(", ", TextCodec.ReadEncodings)}.", statusCode: StatusCodes.Status400BadRequest);
        if (id is null == at is null)
            return TypedResults.Problem("Give the entry's `id` or its path `at`.", statusCode: StatusCodes.Status400BadRequest);
        var opened = await OpenEntryAsync(catalogs, options.CurrentValue, paths, serverFiles, work, unlocks, current, context,
            path, new EntryKey(id, null, at), TextEndpoints.MaxBytes, unlock);
        if (opened.Refused is { } refused)
            return refused.Result switch
            {
                ForbidHttpResult forbid => forbid,
                ProblemHttpResult { StatusCode: StatusCodes.Status413PayloadTooLarge } => TypedResults.Problem(
                    $"It is too large to be shown as text (more than {TextEndpoints.MaxBytes / (1024 * 1024)} MB): download it instead.",
                    statusCode: StatusCodes.Status413PayloadTooLarge, type: TooLargeType),
                ProblemHttpResult problem => problem,
                _ => TypedResults.NotFound(),
            };

        byte[] bytes;
        try
        {
            await using var data = opened.Data!;
            using var buffer = new MemoryStream();
            await data.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }
        catch (ArchiveException error)
        {
            return TypedResults.Problem(error.Message, statusCode: StatusCodes.Status422UnprocessableEntity, type: UnreadableType);
        }
        if (TextCodec.Decode(bytes, encoding) is not { } decoded)
            return TypedResults.Problem("It is not a text file.", statusCode: StatusCodes.Status415UnsupportedMediaType, type: TextEndpoints.NotTextType);

        var archive = new FileInfo(opened.Full);
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(new TextFile(decoded.Text, decoded.Encoding, decoded.LineEnding, EntryTag(opened.Canonical, archive, opened.File!.Id), bytes.Length,
            opened.File!.Entry.Time ?? archive.LastWriteTimeUtc, new TextAccess(false, false, false, false),
            decoded.LineEnding == "mixed" ? TextCodec.DominantLineEnding(decoded.Text) : decoded.LineEnding));
    }

    /// <summary>An entry's version: the archive as it is (its catalog's key) and the entry's id.</summary>
    private static string EntryTag(string canonical, FileInfo archive, long id) =>
        $"{ArchiveCatalogs.KeyOf(canonical, archive.Length, archive.LastWriteTimeUtc)[..16]}-{id:x}";

    /// <summary>An entry by its id (and its name, when given: it must be that), or by its path in the archive.</summary>
    private sealed record EntryKey(long? Id, string? Name, string? At);

    /// <param name="Refused">Why it isn't opened (404, 403, a problem); else <paramref name="Data"/> is the entry's
    /// content, held to its declared size.</param>
    private sealed record OpenedEntry(
        Results<FileStreamHttpResult, NotFound, ForbidHttpResult, ProblemHttpResult>? Refused, Stream? Data, ArchiveFile? File, string Full, string Canonical);

    /// <summary>
    /// A file of an archive opened to be read (<c>files.view</c> and <c>files.download</c> at the archive): only from a
    /// list already read (it never starts one), found as <paramref name="key"/> says; refused larger than
    /// <paramref name="maxSize"/> (413), claiming more than the compression ratio allows (422), encrypted without the
    /// password (403), or when reaching it would unpack too much before it (413). <paramref name="stored"/>: a stored,
    /// unencrypted zip entry comes as a seekable slice of the archive, whatever its size (nothing is unpacked).
    /// </summary>
    private static async Task<OpenedEntry> OpenEntryAsync(
        ArchiveCatalogs catalogs, SfeOptions options, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work,
        ArchiveUnlocks unlocks, CurrentAccess current, HttpContext context, string? path, EntryKey key, long? maxSize, string? unlock,
        bool stored = false)
    {
        var (full, canonical, refused) = await ResolveAsync(paths, serverFiles, work, current, path, Rights.ViewFiles, Rights.Download);
        if (refused is { } forbidden)
            return new(forbidden ? TypedResults.Forbid() : TypedResults.NotFound(), null, null, full, canonical);
        OpenedEntry Problem(string message, int status, string? type = null) =>
            new(TypedResults.Problem(message, statusCode: status, type: type), null, null, full, canonical);

        var settings = options.Files.Archives;
        var password = unlocks.PasswordFor(unlock, (await current.GetAsync()).TenantId, ArchiveUnlocks.OwnerOf(context), full);
        if (FormatOf(full, options) is not { } format
            // An entry comes from a list already read: it never starts a read.
            || CatalogOf(catalogs, full, canonical, format, unlock, password, static () => false) is not { Status: ArchiveCatalogStatus.Ready, Catalog: { } catalog }
            || (key.At is { } at ? catalog.FindFile(at) : catalog.Entry(key.Id ?? 0)) is not { Entry.Kind: ArchiveEntryKind.File } file
            || key.Name is not null && !string.Equals(file.Name, key.Name, StringComparison.Ordinal))
            return new(TypedResults.NotFound(), null, null, full, canonical);

        var entry = file.Entry;
        // Per-entry packed sizes exist only where the format keeps them (zip, non-solid): elsewhere the declared size holds.
        if (entry.Packed > 0 && entry.Size / entry.Packed > settings.MaxCompressionRatio)
            return Problem("This entry claims to be far larger than its packed data: the archive looks hostile.", StatusCodes.Status422UnprocessableEntity, UnreadableType);
        if (stored && !entry.Encrypted)
        {
            try
            {
                if (ArchiveReader.OpenStored(full, format, entry) is { } slice)
                    return new(null, slice, file, full, canonical);
            }
            catch (ArchiveException error)
            {
                return Problem(error.Message, StatusCodes.Status422UnprocessableEntity, UnreadableType);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Problem("The archive can't be read now (in use, or the server may not read it).", StatusCodes.Status500InternalServerError);
            }
        }
        if (maxSize is { } most && entry.Size > most)
            return Problem($"It is too large to be opened from the archive (more than {most / (1024 * 1024)} MB): download it.", StatusCodes.Status413PayloadTooLarge, TooLargeType);
        if (entry.Encrypted && password is null)
            return Problem("This file is protected by a password: open the archive with it first.", StatusCodes.Status403Forbidden, WrongPasswordType);

        try
        {
            return new(null, ArchiveReader.OpenEntry(full, format, entry, password, settings.MaxSkippedMegabytes * 1024L * 1024,
                settings.MaxSevenZipListMegabytes * 1024L * 1024), file, full, canonical);
        }
        catch (ArchiveException error) when (error.TooFar)
        {
            return Problem(error.Message, StatusCodes.Status413PayloadTooLarge, TooLargeType);
        }
        catch (ArchiveException error)
        {
            return Problem(error.Message, StatusCodes.Status422UnprocessableEntity, UnreadableType);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Problem("The archive can't be read now (in use, or the server may not read it).", StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>The archive's format when the viewer is offered for it (the client's rule, checked here on its bytes).</summary>
    private static ArchiveFormat? FormatOf(string full, SfeOptions options)
    {
        ArchiveFormat? format;
        try
        {
            format = ArchiveFormats.Detect(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return format is { } found && ArchiveFormats.IsExplorable(found, new FileInfo(full).Length, options.Files.Archives) ? found : null;
    }

    private static async Task<(string Full, string Canonical, bool? Refused)> ResolveAsync(
        LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, CurrentAccess current, string? path, params string[] rights)
    {
        var state = await current.GetAsync();
        if (!paths.TryResolve(path, out var full) || paths.ToRelative(full) is not { Length: > 0 } canonical
            || !File.Exists(full) || FileEndpoints.Protected(full, serverFiles, work))
            return (string.Empty, string.Empty, false);
        foreach (var right in rights)
            if (state.Check(paths, canonical, right) is var verdict and not PathVerdict.Allowed)
                return (full, canonical, verdict != PathVerdict.NotFound);
        return (full, canonical, null);
    }

    private static ArchiveItem ToItem(ArchiveFolder folder) =>
        new("folder", folder.Id, folder.Name, folder.Size, folder.Packed, folder.Time, false, folder.Folders, folder.Files, folder.AllFiles);

    private static ArchiveItem ToItem(ArchiveFile file, MediaClassifier media) => new(
        file.Entry.Kind switch { ArchiveEntryKind.File => "file", ArchiveEntryKind.Link => "link", _ => "other" },
        file.Id, file.Name, file.Entry.Size, file.Entry.Packed, file.Entry.Time, file.Entry.Encrypted,
        Media: file.Entry.Kind != ArchiveEntryKind.File ? null : media.Classify(Path.GetExtension(file.Name)) switch
        {
            ItemKind.Image => "image",
            ItemKind.Video => "video",
            ItemKind.Audio => "audio",
            _ => null,
        });

    private static string FormatName(ArchiveFormat format) => format switch
    {
        ArchiveFormat.Zip => "zip",
        ArchiveFormat.SevenZip => "7z",
        ArchiveFormat.Rar => "rar",
        ArchiveFormat.Tar => "tar",
        ArchiveFormat.TarGzip => "tar.gz",
        ArchiveFormat.TarBzip2 => "tar.bz2",
        ArchiveFormat.TarXz => "tar.xz",
        _ => "other",
    };

    private static ProblemHttpResult NotExplorable() => TypedResults.Problem(
        "This file can't be explored: it isn't an archive SFE reads, or listing it would mean unpacking all of it (extract it instead).",
        statusCode: StatusCodes.Status409Conflict, type: NotExplorableType);

    /// <summary>A page's end as an opaque token: <c>d</c>/<c>f</c> (folders, files), the id, the name.</summary>
    internal static string Cursor(ArchiveCursor cursor) =>
        Base64Url($"{(cursor.InFiles ? 'f' : 'd')}{cursor.Id}\n{cursor.Name}");

    internal static ArchiveCursor? ParseCursor(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 8192 || EpubEntries.PathOf(token) is not { Length: >= 2 } text)
            return null;
        var line = text.IndexOf('\n');
        if (line < 2 || text[0] is not ('d' or 'f') || !long.TryParse(text.AsSpan(1, line - 1), out var id))
            return null;
        return new ArchiveCursor(text[0] == 'f', text[(line + 1)..], id);
    }

    private static string Base64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
