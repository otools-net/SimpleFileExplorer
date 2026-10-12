using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Sfe.Api.Security;
using Sfe.Api.Tenancy;
using Sfe.Core.Access;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Search;
using Sfe.Core.Settings;
using Sfe.Core.Tagging;
using Sfe.Core.Thumbnails;

namespace Sfe.Api.Endpoints;

/// <param name="Paths">Folders to describe, relative to the library root.</param>
public sealed record FolderCoversRequest(IReadOnlyList<string>? Paths);

/// <param name="Path">File, relative to the library root.</param>
public sealed record FileLinkRequest(string? Path);

public static class LibraryEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = CreateContentTypes();

    /// <summary>Images (thumbnails and pictures) are not cached by the browser.</summary>
    private const string NoStore = "no-store";

    public static RouteGroupBuilder MapLibraryEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/browse", Browse)
            .RequireRight(Rights.Browse)
            .WithName("Browse")
            .WithTags("Library")
            .WithSummary("Lists a folder of the library (folders first).");

        api.MapGet("/browse/siblings", Siblings)
            .RequireRight(Rights.Browse)
            .WithName("GetFolderSiblings")
            .WithTags("Library")
            .WithSummary("The folders before and after a folder in its parent.")
            .WithDescription("""
                Pass the `sort` and `desc` the parent is listed with (as for `/browse`): the siblings follow that order.
                The library root has no siblings.
                """);

        api.MapPost("/browse/covers", Covers)
            .RequireRight(Rights.Browse)
            .WithName("GetFolderCovers")
            .WithTags("Library")
            .WithSummary("The first pictures of folders, shown on their cards.")
            .WithDescription($"""
                Takes up to {LibraryBrowser.MaxCoverPaths} folders and returns, for each one that exists, its first
                `Browse:FolderPictures` images and videos in name order (videos only when thumbnails are available).
                Only a folder's own files count; a folder without any, or a setting of 0, gets an empty list.
                """);

        api.MapGet("/search", Search)
            .RequireRight(Rights.Browse)
            .WithName("Search")
            .WithTags("Library")
            .WithSummary("Searches the file index.")
            .WithDescription("""
                `,` separates alternatives (OR), a space separates required terms (AND), `-term` excludes,
                `s:<10mb` / `s:>=1gb` / `s:700mb` filter by size, `e:mp4` by extension, `w:>=1920` / `h:<720` /
                `r:1920x1080` by resolution, a leading `*` matches the whole path instead of the file name, `t:name`
                (or `tag:"two words"`) finds files with a tag or any tag inheriting from it, and `special:untagged`
                files without tags. A `-` before any term (`-e:mp4`, `-s:>1gb`, `-w:1920`, `-t:name`) finds the files
                that term doesn't. Files with a hidden tag (Archived) are left out unless the search names that tag.

                `sort` is `path` (default, grouped by folder), `name`, `modified`, `size` or `type`; `desc` reverses it.
                Pages follow each other by passing the previous page's `next` as `after`, with the same sort.
                The first page also gives `total`, the number of matches counted up to 10,000 (`totalCapped` when there
                are more); the next pages leave it null.
                """);

        api.MapGet("/files/raw/{**path}", Raw)
            .RequireRight(Rights.ViewFiles)
            .WithName("GetFile")
            .WithTags("Library")
            .WithSummary("Streams a file (supports HTTP range requests for media seeking).")
            .WithDescription("`download=true` asks the browser to save it, which needs the right `files.download`.");

        api.MapPost("/files/links", CreateLink)
            .RequireRight(Rights.ExternalPlayer)
            .WithName("CreateFileLink")
            .WithTags("Library")
            .WithSummary("A link to a file that works without signing in, for a while: for external players.")
            .WithDescription("""
                The link (`api/files/shared/{token}/{name}`, relative to the app) works for `Player:LinkLifetimeMinutes`.
                It stops working earlier when the account that asked for it changes its password, is disabled, or loses the
                right to view files or use external players.
                """);

        api.MapGet("/files/shared/{token}/{**name}", Shared)
            .AllowAnonymous()
            .WithMetadata(new RightMetadata([Rights.ExternalPlayer]))
            .WithName("GetSharedFile")
            .WithTags("Library")
            .WithSummary("Streams the file of a link made by `CreateFileLink` (the token stands for the sign-in; range requests supported).");

        api.MapGet("/thumbnails/{**path}", Thumbnail)
            .RequireRight(Rights.ViewFiles)
            .WithName("GetThumbnail")
            .WithTags("Library")
            .WithSummary("JPEG preview of a video or image.");

        return api;
    }

    private static async Task<Results<Ok<DirectoryListing>, NotFound>> Browse(
        LibraryBrowser browser,
        TagService tags,
        IOptionsMonitor<SfeOptions> options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken,
        string? path = null,
        int offset = 0,
        int? limit = null,
        string? sort = null,
        bool desc = false)
    {
        // Parsed by hand: enum binding of query values is case-sensitive and the client sends "size", "name"...
        var sortField = Enum.TryParse<SortField>(sort, ignoreCase: true, out var parsed) ? parsed : SortField.Name;
        var page = options.CurrentValue.Browse;
        var listing = browser.List(path, Math.Max(0, offset), Clamp(limit, page.DefaultPageSize, page.MaxPageSize), sortField, desc);
        if (listing is null)
            return TypedResults.NotFound();

        try
        {
            listing = listing with { Items = await tags.AttachIndexDataAsync(listing.Items, cancellationToken) };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Browsing works without the index: list the folder without tags or resolutions.
            loggerFactory.CreateLogger(typeof(LibraryEndpoints)).LogWarning(ex, "Cannot read the tags and resolutions of {Path}", path);
        }

        return TypedResults.Ok(listing);
    }

    private static Results<Ok<FolderSiblings>, NotFound> Siblings(LibraryBrowser browser, string? path = null, string? sort = null, bool desc = false)
    {
        var sortField = Enum.TryParse<SortField>(sort, ignoreCase: true, out var parsed) ? parsed : SortField.Name;
        var siblings = browser.Siblings(path, sortField, desc);
        return siblings is null ? TypedResults.NotFound() : TypedResults.Ok(siblings);
    }

    private static Results<Ok<IReadOnlyList<FolderCover>>, ValidationProblem> Covers(
        LibraryBrowser browser, ThumbnailService thumbnails, FolderCoversRequest? request)
    {
        var paths = request?.Paths ?? [];
        if (paths.Count > LibraryBrowser.MaxCoverPaths)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["paths"] = [$"At most {LibraryBrowser.MaxCoverPaths} paths."] });

        return TypedResults.Ok(browser.Covers(paths, videos: thumbnails.IsAvailable));
    }

    private static async Task<Results<Ok<SearchResults>, NotFound>> Search(
        FileSearchService search,
        IOptionsMonitor<SfeOptions> options,
        CancellationToken cancellationToken,
        string? q = null,
        string? path = null,
        string? after = null,
        int? limit = null,
        string? sort = null,
        bool desc = false)
    {
        var sortField = Enum.TryParse<SortField>(sort, ignoreCase: true, out var parsed) ? parsed : SortField.Path;
        var page = options.CurrentValue.Search;
        var results = await search.SearchAsync(
            q, path, string.IsNullOrEmpty(after) ? null : after, Clamp(limit, page.DefaultPageSize, page.MaxPageSize), sortField, desc, cancellationToken);
        return results is null ? TypedResults.NotFound() : TypedResults.Ok(results);
    }

    private static async Task<Results<PhysicalFileHttpResult, NotFound, ForbidHttpResult>> Raw(
        LibraryPaths paths, CurrentAccess current, HttpContext context, string path, bool download = false)
    {
        if (!paths.TryResolve(path, out var fullPath) || !File.Exists(fullPath))
            return TypedResults.NotFound();
        if (download && !(await current.GetAsync()).Has(Rights.Download))
            return TypedResults.Forbid();

        return ServeFile(context, fullPath, download);
    }

    private static async Task<Results<Ok<FileLink>, NotFound>> CreateLink(
        LibraryPaths paths, CurrentAccess current, FileLinks links, IOptionsMonitor<SfeOptions> options, FileLinkRequest request)
    {
        var path = LibraryPaths.NormalizeRelative(request.Path);
        if (path is null || !paths.TryResolve(path, out var fullPath) || !File.Exists(fullPath))
            return TypedResults.NotFound();

        var state = await current.GetAsync();
        var lifetime = TimeSpan.FromMinutes(Math.Clamp(options.CurrentValue.Player.LinkLifetimeMinutes, 1, SettingsService.MaxLinkLifetimeMinutes));
        return TypedResults.Ok(links.Create(state.TenantId, state.User, path, lifetime));
    }

    private static async Task<Results<PhysicalFileHttpResult, NotFound, ProblemHttpResult>> Shared(
        HttpContext context, FileLinks links, AccessService accounts, RightsEvaluator rights, TenantRegistry tenants, string token, string name)
    {
        if (links.Read(token) is not { } claims)
            return TypedResults.NotFound();

        // The account as it is now: a new password or a disabled account ends the links it gave out.
        if (claims.User is { } userId)
        {
            var user = await accounts.GetSessionUserAsync(userId, context.RequestAborted);
            if (user is null || user.Disabled || user.SecurityStamp != claims.Stamp)
                return TypedResults.Problem("This link no longer works: ask for a new one.", statusCode: StatusCodes.Status403Forbidden);
        }

        var access = await rights.GetAsync(new AccessSubject(claims.Tenant, claims.User), context.RequestAborted);
        if (!access.Has(Rights.ViewFiles) || !access.Has(Rights.ExternalPlayer))
            return TypedResults.Problem("This link no longer works: ask for a new one.", statusCode: StatusCodes.Status403Forbidden);

        var paths = (await tenants.GetAsync(claims.Tenant, context.RequestAborted))?.Services.GetRequiredService<LibraryPaths>();
        if (paths is null || !paths.TryResolve(claims.Path, out var fullPath) || !File.Exists(fullPath))
            return TypedResults.NotFound();

        return ServeFile(context, fullPath, download: false);
    }

    /// <summary>A file of the library, with range requests (seeking in media) and the right type for players.</summary>
    internal static PhysicalFileHttpResult ServeFile(HttpContext context, string fullPath, bool download)
    {
        if (!ContentTypes.TryGetContentType(fullPath, out var contentType))
            contentType = "application/octet-stream";

        // Pictures are not kept by the browser: an edited or replaced photo shows up as it is now.
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            context.Response.Headers[HeaderNames.CacheControl] = NoStore;

        return TypedResults.PhysicalFile(
            fullPath,
            contentType,
            fileDownloadName: download ? Path.GetFileName(fullPath) : null,
            lastModified: File.GetLastWriteTimeUtc(fullPath),
            enableRangeProcessing: true);
    }

    private static async Task<Results<PhysicalFileHttpResult, NotFound>> Thumbnail(
        ThumbnailService thumbnails,
        HttpContext context,
        string path,
        CancellationToken cancellationToken)
    {
        var thumbnail = await thumbnails.GetAsync(path, cancellationToken);
        if (thumbnail is null)
            return TypedResults.NotFound();

        // The disk cache is the only thumbnail cache: the browser asks again, so a regenerated thumbnail shows at once.
        context.Response.Headers[HeaderNames.CacheControl] = NoStore;
        return TypedResults.PhysicalFile(thumbnail, "image/jpeg", lastModified: File.GetLastWriteTimeUtc(thumbnail));
    }

    private static int Clamp(int? requested, int fallback, int max) =>
        Math.Clamp(requested ?? fallback, 1, Math.Max(1, max));

    private static FileExtensionContentTypeProvider CreateContentTypes()
    {
        var provider = new FileExtensionContentTypeProvider();
        // Labelling Matroska as WebM lets browsers try to play it.
        provider.Mappings[".mkv"] = "video/webm";
        provider.Mappings[".webm"] = "video/webm";
        provider.Mappings[".ts"] = "video/mp2t";
        provider.Mappings[".flv"] = "video/x-flv";
        provider.Mappings[".m4v"] = "video/mp4";
        provider.Mappings[".avif"] = "image/avif";
        provider.Mappings[".opus"] = "audio/ogg";
        provider.Mappings[".flac"] = "audio/flac";
        return provider;
    }
}
