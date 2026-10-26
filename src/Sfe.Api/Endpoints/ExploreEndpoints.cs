using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
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
public sealed record ArchiveItem(
    string Kind, long Id, string Name, long Size, long Packed, DateTime? Modified, bool Encrypted,
    long? Folders = null, long? Files = null, long? AllFiles = null);

public sealed record ArchiveCrumb(long Id, string Name);

/// <param name="Skipped">Folders between the root and <paramref name="Last"/> (deep trees: the way is shown collapsed).</param>
public sealed record ArchiveTrail(int Skipped, IReadOnlyList<ArchiveCrumb> Last);

/// <param name="Complete">False when the list stopped (a limit, damage): <paramref name="Stopped"/> says why.</param>
/// <param name="Folder">The folder listed: its totals (<c>AllFiles</c>, <c>Size</c>: its whole tree).</param>
/// <param name="Next">The cursor of the next page; null on the last one.</param>
public sealed record ArchiveListing(
    string Format, bool Complete, string? Stopped, bool NeedsPassword, long Entries, long LeftOut,
    ArchiveItem Folder, ArchiveTrail Trail, IReadOnlyList<ArchiveItem> Items, string? Next);

/// <param name="Read">Entries read so far.</param>
public sealed record ArchiveReading(long Read);

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
    public const int PageSize = 200;
    public const int MaxPageSize = 1000;

    public static RouteGroupBuilder MapExploreEndpoints(this RouteGroupBuilder api)
    {
        var explore = api.MapGroup("/files/explore").WithTags("Files");

        explore.MapGet("", List)
            .RequireRight(Rights.ViewFiles)
            .FolderScoped("files.view at the archive")
            .WithName("ExploreArchive")
            .WithSummary("A folder of an archive (zip, 7z, rar, tar), in pages: its folders first, then its files, by name.")
            .WithDescription($"""
                `folder` is a folder's id (from a previous answer; the root without it) or `at` its path inside the archive.
                The first question about an archive reads its list once, in the background: 202 with the entries read so
                far until it is ready (ask again). `after` is the previous page's `next`; `limit` at most {MaxPageSize}
                (default {PageSize}). 409 (`{NotExplorableType}`) for a file that isn't an archive SFE explores (one file
                compressed alone, a compressed tar over `Files:Archives:MaxCompressedTarMegabytes`, a name its bytes deny);
                422 (`{UnreadableType}`) when it can't be read now. `complete` false: the list stopped (`stopped` says why).
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
                claiming more than `Files:Archives:MaxCompressionRatio` times its packed size is refused (422). Its data is
                held to the size the archive declares: more or less is a broken archive, and the download is cut off.
                """);
        return api;
    }

    private static async Task<Results<Ok<ArchiveListing>, Accepted<ArchiveReading>, NotFound, ForbidHttpResult, ProblemHttpResult>> List(
        ArchiveCatalogs catalogs, IOptionsMonitor<SfeOptions> options, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work,
        CurrentAccess current, HttpContext context, string? path, long? folder, string? at, string? after, int? limit)
    {
        var (full, canonical, refused) = await ResolveAsync(paths, serverFiles, work, current, path, Rights.ViewFiles);
        if (refused is { } forbidden)
            return forbidden ? TypedResults.Forbid() : TypedResults.NotFound();
        if (FormatOf(full, options.CurrentValue) is not { } format)
            return NotExplorable();

        var state = catalogs.Get(full, canonical, format);
        context.Response.Headers.CacheControl = "no-store";
        if (state.Status == ArchiveCatalogStatus.Building)
            return TypedResults.Accepted((string?)null, new ArchiveReading(state.Read));
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
        var items = page.Folders.Select(ToItem).Concat(page.Files.Select(ToItem)).ToList();
        return TypedResults.Ok(new ArchiveListing(
            FormatName(format), summary.Complete, summary.Stopped, summary.NeedsPassword, summary.Entries, summary.LeftOut,
            ToItem(shown), new ArchiveTrail(trail.Skipped, [.. trail.Last.Select(crumb => new ArchiveCrumb(crumb.Id, crumb.Name))]),
            items, page.Next is { } next ? Cursor(next) : null));
    }

    private static async Task<Results<FileStreamHttpResult, NotFound, ForbidHttpResult, ProblemHttpResult>> GetEntry(
        ArchiveCatalogs catalogs, IOptionsMonitor<SfeOptions> options, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work,
        CurrentAccess current, HttpContext context, string archive, long id, string name, bool? download)
    {
        var (full, canonical, refused) = await ResolveAsync(paths, serverFiles, work, current, EpubEntries.PathOf(archive), Rights.ViewFiles, Rights.Download);
        if (refused is { } forbidden)
            return forbidden ? TypedResults.Forbid() : TypedResults.NotFound();
        var settings = options.CurrentValue.Files.Archives;
        if (FormatOf(full, options.CurrentValue) is not { } format
            || catalogs.Get(full, canonical, format) is not { Status: ArchiveCatalogStatus.Ready, Catalog: { } catalog }
            || catalog.Entry(id) is not { Entry.Kind: ArchiveEntryKind.File } file
            || !string.Equals(file.Name, name, StringComparison.Ordinal))
            return TypedResults.NotFound();

        var entry = file.Entry;
        // Per-entry packed sizes exist only where the format keeps them (zip, non-solid): elsewhere the declared size holds.
        if (entry.Packed > 0 && entry.Size / entry.Packed > settings.MaxCompressionRatio)
            return TypedResults.Problem("This entry claims to be far larger than its packed data: the archive looks hostile.",
                statusCode: StatusCodes.Status422UnprocessableEntity, type: UnreadableType);
        var contentType = LibraryEndpoints.ContentTypeOf(file.Name);
        var inline = download != true && LibraryEndpoints.OpensInline(contentType);
        if (download != true && entry.Size > settings.MaxPreviewMegabytes * 1024L * 1024)
            return TypedResults.Problem($"It is too large to be opened from the archive (more than {settings.MaxPreviewMegabytes} MB): download it.",
                statusCode: StatusCodes.Status413PayloadTooLarge, type: TooLargeType);

        Stream data;
        try
        {
            data = ArchiveReader.OpenEntry(full, format, entry);
        }
        catch (ArchiveException error)
        {
            return TypedResults.Problem(error.Message, statusCode: StatusCodes.Status422UnprocessableEntity, type: UnreadableType);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return TypedResults.Problem("The archive can't be read now (in use, or the server may not read it).", statusCode: StatusCodes.Status500InternalServerError);
        }

        var info = new FileInfo(full);
        context.Response.Headers[HeaderNames.ContentSecurityPolicy] = SecurityHeaders.FilePolicy;
        context.Response.Headers[HeaderNames.CacheControl] = "private, no-cache";
        return TypedResults.Stream(data, contentType, inline ? null : file.Name, lastModified: info.LastWriteTimeUtc,
            entityTag: new EntityTagHeaderValue($"\"{ArchiveCatalogs.KeyOf(canonical, info.Length, info.LastWriteTimeUtc)[..16]}-{id:x}\""));
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

    private static ArchiveItem ToItem(ArchiveFile file) => new(
        file.Entry.Kind switch { ArchiveEntryKind.File => "file", ArchiveEntryKind.Link => "link", _ => "other" },
        file.Id, file.Name, file.Entry.Size, file.Entry.Packed, file.Entry.Time, file.Entry.Encrypted);

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
