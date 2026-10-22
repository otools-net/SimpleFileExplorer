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

/// <param name="Animated">The picture has more than one frame.</param>
public sealed record ImageAnimationInfo(bool Animated);

public static class LibraryEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = CreateContentTypes();

    /// <summary>Images (thumbnails and pictures) are not cached by the browser.</summary>
    private const string NoStore = "no-store";

    public static RouteGroupBuilder MapLibraryEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/browse", Browse)
            .RequireRight(Rights.Browse)
            .FolderScoped("404 for a folder the subject doesn't see; hidden entries left out of the listing and its counts")
            .WithName("Browse")
            .WithTags("Library")
            .WithSummary("Lists a folder of the library (folders first).");

        api.MapGet("/browse/siblings", Siblings)
            .RequireRight(Rights.Browse)
            .FolderScoped("404 for a folder the subject doesn't see; hidden siblings skipped")
            .WithName("GetFolderSiblings")
            .WithTags("Library")
            .WithSummary("The folders before and after a folder in its parent.")
            .WithDescription("""
                Pass the `sort` and `desc` the parent is listed with (as for `/browse`): the siblings follow that order.
                The library root has no siblings.
                """);

        api.MapPost("/browse/covers", Covers)
            .RequireRateLimiting(RequestLimits.Media)
            .RequireRight(Rights.Browse)
            .FolderScoped("folders the subject doesn't browse get no cover; hidden pictures left out")
            .WithName("GetFolderCovers")
            .WithTags("Library")
            .WithSummary("The first pictures of folders, shown on their cards.")
            .WithDescription($"""
                Takes up to {LibraryBrowser.MaxCoverPaths} folders and returns, for each one that exists, its first
                `Browse:FolderPictures` images and videos in name order (videos only when thumbnails are available).
                Only a folder's own files count; a folder without any, or a setting of 0, gets an empty list.
                """);

        api.MapGet("/search", Search)
            .RequireRateLimiting(RequestLimits.Search)
            .RequireRight(Rights.Browse)
            .FolderScoped("404 for a scope the subject doesn't see; only the files it sees are searched")
            .WithName("Search")
            .WithTags("Library")
            .WithSummary("Searches the file index.")
            .WithDescription("""
                `,` separates alternatives (OR), a space separates required terms (AND), `-term` excludes,
                `s:<10mb` / `s:>=1gb` / `s:700mb` filter by size, `e:mp4` by extension, `w:>=1920` / `h:<720` /
                `r:1920x1080` by resolution, `d:>10m` by duration, `type:image` / `type:video` / `type:audio` /
                `type:other` by kind of file, a leading `*` matches the whole path instead of the file name, `t:name`
                (or `tag:"two words"`) finds files with a tag or any tag inheriting from it, and `special:untagged`
                files without tags. A `-` before any term (`-e:mp4`, `-s:>1gb`, `-w:1920`, `-t:name`) finds the files
                that term doesn't. Files with a hidden tag (Archived) are left out unless the search names that tag.

                `sort` is `path` (default, grouped by folder), `name`, `modified`, `size`, `type`, `duration` or
                `resolution` (width × height); `desc` reverses it. The last two list the files whose value is known
                first, then the others by path, in both directions.
                Pages follow each other by passing the previous page's `next` as `after`, with the same sort.
                The first page also gives `total`, the number of matches counted up to 10,000 (`totalCapped` when there
                are more); the next pages leave it null.
                """);

        api.MapGet("/files/raw/{**path}", Raw)
            .RequireRight(Rights.ViewFiles)
            .FolderScoped("files.view (files.download to download) at the file")
            .WithName("GetFile")
            .WithTags("Library")
            .WithSummary("Streams a file (supports HTTP range requests for media seeking).")
            .WithDescription("`download=true` asks the browser to save it, which needs the right `files.download`.");

        api.MapGet("/files/animated/{**path}", Animated)
            .RequireRateLimiting(RequestLimits.Media)
            .RequireRight(Rights.ViewFiles)
            .FolderScoped("files.view at the file")
            .WithName("GetImageAnimation")
            .WithTags("Library")
            .WithSummary("Whether a picture is animated (GIF, APNG, WebP or AVIF with more than one frame).")
            .WithDescription("""
                Read from the file's structure, without decoding it. The listings ask before playing the original of an
                image they show through its thumbnail, on hover.
                """);

        api.MapPost("/files/links", CreateLink)
            .RequireRight(Rights.ExternalPlayer)
            .FolderScoped("files.view and files.external at the file")
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
            .FolderScoped("the token's subject: files.view and files.external at the file, as the rules are now")
            .WithName("GetSharedFile")
            .WithTags("Library")
            .WithSummary("Streams the file of a link made by `CreateFileLink` (the token stands for the sign-in; range requests supported).");

        api.MapGet("/thumbnails/{**path}", Thumbnail)
            .RequireRateLimiting(RequestLimits.Media)
            .RequireRight(Rights.ViewFiles)
            .FolderScoped("files.view at the file")
            .WithName("GetThumbnail")
            .WithTags("Library")
            .WithSummary("JPEG preview of a video or image.");

        return api;
    }

    /// <summary>The problem type of a browse whose library folder doesn't exist (moved drive, folder renamed, new install).</summary>
    public const string LibraryRootMissingType = "library-root-missing";

    private static async Task<Results<Ok<DirectoryListing>, NotFound, ProblemHttpResult>> Browse(
        LibraryBrowser browser,
        LibraryPaths paths,
        TagService tags,
        FolderDefaultTags defaultTags,
        CurrentAccess current,
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
        var state = await current.GetAsync();
        if (FolderListing(state, paths, path) is not { } folder)
            return TypedResults.NotFound();
        var listing = browser.List(path, Math.Max(0, offset), Clamp(limit, page.DefaultPageSize, page.MaxPageSize), sortField, desc, folder.Filter);
        if (listing is not null && !state.Access.FolderPolicy.IsUnrestricted && state.At(paths, path) is { } here)
        {
            // The client shows only what the asker may do here: tag, download, edit...
            var children = state.Access.FolderPolicy.ChildrenWithRules(listing.Path, state.Access.Rights)
                .Where(c => c.Value.IsVisible)
                .ToDictionary(c => c.Key, c => new FolderAccessView([.. c.Value.Allowed.Order(StringComparer.Ordinal)], c.Value.PathOnly), LibraryPaths.PathComparer);
            listing = listing with { Access = new FolderAccessView([.. here.Allowed.Order(StringComparer.Ordinal)], here.PathOnly, children) };
        }
        if (listing is null)
        {
            // Said apart from a missing sub-folder: the whole library is gone, and an administrator can pick another.
            return LibraryPaths.NormalizeRelative(path) is "" && !Directory.Exists(paths.Root)
                ? TypedResults.Problem($"The library folder {paths.Root} does not exist on the server.", statusCode: StatusCodes.Status404NotFound,
                    type: LibraryRootMissingType)
                : TypedResults.NotFound();
        }

        // Shown to everyone: it tells why a folder has no thumbnails or isn't found by searches.
        listing = FolderExclusions.Of(options.CurrentValue).Mark(listing);

        try
        {
            listing = listing with { Items = await tags.AttachIndexDataAsync(listing.Items, cancellationToken) };
            // As the files' tags: the tags the files put here get.
            listing = await defaultTags.MarkAsync(listing, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Browsing works without the index: list the folder without tags or resolutions.
            loggerFactory.CreateLogger(typeof(LibraryEndpoints)).LogWarning(ex, "Cannot read the tags and resolutions of {Path}", path);
        }

        return TypedResults.Ok(listing);
    }

    private static async Task<Results<Ok<FolderSiblings>, NotFound>> Siblings(
        LibraryBrowser browser, LibraryPaths paths, CurrentAccess current, string? path = null, string? sort = null, bool desc = false)
    {
        var sortField = Enum.TryParse<SortField>(sort, ignoreCase: true, out var parsed) ? parsed : SortField.Name;
        var state = await current.GetAsync();
        ListingFilter? filter = null;
        if (!state.Access.FolderPolicy.IsUnrestricted)
        {
            if (state.At(paths, path) is not { IsVisible: true } || Canonical(paths, path) is not { } folder)
                return TypedResults.NotFound();
            var parent = folder.Contains('/') ? folder[..folder.LastIndexOf('/')] : string.Empty;
            if (folder.Length > 0 && FolderListing(state, paths, parent) is { } listing)
                filter = listing.Filter;
        }
        var siblings = browser.Siblings(path, sortField, desc, filter);
        return siblings is null ? TypedResults.NotFound() : TypedResults.Ok(siblings);
    }

    /// <summary>The path as the file system names it, root-relative; null when not a path of the library.</summary>
    private static string? Canonical(LibraryPaths paths, string? path) =>
        paths.TryResolve(path, out var full) ? paths.ToRelative(full) : null;

    /// <summary>
    /// Whether the subject may list <paramref name="path"/> (also when it only leads to what it sees), and what the
    /// listing leaves out; null when it may not.
    /// </summary>
    private static (ListingFilter? Filter, bool PathOnly)? FolderListing(AccessState state, LibraryPaths paths, string? path)
    {
        if (state.Access.FolderPolicy.IsUnrestricted)
            return (null, false);
        if (state.At(paths, path) is not { IsVisible: true } access || Canonical(paths, path) is not { } folder)
            return null;
        return (state.ListingFilterOf(folder, access), access.PathOnly);
    }

    private static async Task<Results<Ok<IReadOnlyList<FolderCover>>, ValidationProblem>> Covers(
        LibraryBrowser browser, ThumbnailService thumbnails, LibraryPaths paths, CurrentAccess current, FolderCoversRequest? request)
    {
        var folders = request?.Paths ?? [];
        if (folders.Count > LibraryBrowser.MaxCoverPaths)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["paths"] = [$"At most {LibraryBrowser.MaxCoverPaths} paths."] });

        var state = await current.GetAsync();
        if (state.Access.FolderPolicy.IsUnrestricted)
            return TypedResults.Ok(browser.Covers(folders, videos: thumbnails.IsAvailable));
        // A folder only on the way to others shows nothing of its own.
        var covers = browser.Covers(folders.Where(f => state.Sees(paths, f ?? string.Empty)), videos: thumbnails.IsAvailable);
        return TypedResults.Ok<IReadOnlyList<FolderCover>>([.. covers.Select(c => c with { Items = [.. c.Items.Where(i => state.Sees(paths, i.Path))] })]);
    }

    private static async Task<Results<Ok<SearchResults>, NotFound>> Search(
        FileSearchService search,
        LibraryPaths paths,
        CurrentAccess current,
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
        var state = await current.GetAsync();
        if (state.At(paths, path) is not { IsVisible: true })
            return TypedResults.NotFound();
        var results = await search.SearchAsync(
            q, path, string.IsNullOrEmpty(after) ? null : after, Clamp(limit, page.DefaultPageSize, page.MaxPageSize), sortField, desc, cancellationToken,
            state.Visible());
        return results is null ? TypedResults.NotFound() : TypedResults.Ok(results with { Items = state.Seen(paths, results.Items) });
    }

    private static async Task<Results<PhysicalFileHttpResult, NotFound, ForbidHttpResult>> Raw(
        LibraryPaths paths, CurrentAccess current, ServerFiles serverFiles, HttpContext context, string path, bool download = false)
    {
        // The server's own files answer as if they weren't there, wherever a link put them.
        if (!paths.TryResolve(path, out var fullPath) || !File.Exists(fullPath) || serverFiles.Contains(fullPath))
            return TypedResults.NotFound();
        var state = await current.GetAsync();
        if (state.Check(paths, path, Rights.ViewFiles) is var verdict and not PathVerdict.Allowed)
            return verdict == PathVerdict.NotFound ? TypedResults.NotFound() : TypedResults.Forbid();
        if (download && state.Check(paths, path, Rights.Download) != PathVerdict.Allowed)
            return TypedResults.Forbid();

        return ServeFile(context, fullPath, download);
    }

    private static async Task<Results<Ok<ImageAnimationInfo>, NotFound>> Animated(
        LibraryPaths paths, CurrentAccess current, ServerFiles serverFiles, ILoggerFactory loggerFactory, string path, CancellationToken cancellationToken)
    {
        if (!paths.TryResolve(path, out var fullPath) || !File.Exists(fullPath) || serverFiles.Contains(fullPath))
            return TypedResults.NotFound();
        if ((await current.GetAsync()).Check(paths, path, Rights.ViewFiles) != PathVerdict.Allowed)
            return TypedResults.NotFound();

        try
        {
            await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, useAsync: false);
            return TypedResults.Ok(new ImageAnimationInfo(await Task.Run(() => ImageAnimation.IsAnimated(stream), cancellationToken)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable now (locked, drive gone): shown as still, the thumbnail stays.
            loggerFactory.CreateLogger(typeof(LibraryEndpoints)).LogWarning(ex, "Cannot tell whether {Path} is animated", path);
            return TypedResults.Ok(new ImageAnimationInfo(false));
        }
    }

    private static async Task<Results<Ok<FileLink>, NotFound, ForbidHttpResult>> CreateLink(
        LibraryPaths paths, CurrentAccess current, FileLinks links, ServerFiles serverFiles, IOptionsMonitor<SfeOptions> options, FileLinkRequest request)
    {
        var path = LibraryPaths.NormalizeRelative(request.Path);
        if (path is null || !paths.TryResolve(path, out var fullPath) || !File.Exists(fullPath) || serverFiles.Contains(fullPath))
            return TypedResults.NotFound();

        var state = await current.GetAsync();
        if (state.Check(paths, path, Rights.ViewFiles) is var verdict and not PathVerdict.Allowed)
            return verdict == PathVerdict.NotFound ? TypedResults.NotFound() : TypedResults.Forbid();
        if (state.Check(paths, path, Rights.ExternalPlayer) != PathVerdict.Allowed)
            return TypedResults.Forbid();
        var lifetime = TimeSpan.FromMinutes(Math.Clamp(options.CurrentValue.Player.LinkLifetimeMinutes, 1, SettingsService.MaxLinkLifetimeMinutes));
        return TypedResults.Ok(links.Create(state.TenantId, state.User, path, lifetime));
    }

    private static async Task<Results<PhysicalFileHttpResult, NotFound, ProblemHttpResult>> Shared(
        HttpContext context, FileLinks links, AccessService accounts, RightsEvaluator rights, TenantRegistry tenants, ServerFiles serverFiles,
        string token, string name)
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
        if (paths is null || !paths.TryResolve(claims.Path, out var fullPath) || !File.Exists(fullPath) || serverFiles.Contains(fullPath))
            return TypedResults.NotFound();
        // The folder rules as they are now: a rule made after the link hides the file from it too.
        var subject = new AccessState(null, null, access);
        if (subject.Check(paths, claims.Path, Rights.ViewFiles) != PathVerdict.Allowed || subject.Check(paths, claims.Path, Rights.ExternalPlayer) != PathVerdict.Allowed)
            return TypedResults.Problem("This link no longer works: ask for a new one.", statusCode: StatusCodes.Status403Forbidden);

        return ServeFile(context, fullPath, download: false);
    }

    /// <summary>
    /// A file of the library, with range requests (seeking in media) and the right type for players. Its content is
    /// anyone's who can write in the library: it is sandboxed (<see cref="SecurityHeaders.FilePolicy"/>), and a type the
    /// browser could run as a page (HTML, SVG, XML...) is only ever downloaded (<see cref="OpensInline"/>).
    /// </summary>
    internal static PhysicalFileHttpResult ServeFile(HttpContext context, string fullPath, bool download)
    {
        if (!ContentTypes.TryGetContentType(fullPath, out var contentType))
            contentType = "application/octet-stream";

        // Pictures are not kept by the browser: an edited or replaced photo shows up as it is now.
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            context.Response.Headers[HeaderNames.CacheControl] = NoStore;
        context.Response.Headers[HeaderNames.ContentSecurityPolicy] = SecurityHeaders.FilePolicy;

        return TypedResults.PhysicalFile(
            fullPath,
            contentType,
            fileDownloadName: download || !OpensInline(contentType) ? Path.GetFileName(fullPath) : null,
            lastModified: File.GetLastWriteTimeUtc(fullPath),
            enableRangeProcessing: true);
    }

    private static async Task<Results<PhysicalFileHttpResult, NotFound>> Thumbnail(
        ThumbnailService thumbnails,
        LibraryPaths paths,
        CurrentAccess current,
        HttpContext context,
        string path,
        CancellationToken cancellationToken)
    {
        if ((await current.GetAsync()).Check(paths, path, Rights.ViewFiles) != PathVerdict.Allowed)
            return TypedResults.NotFound();
        var thumbnail = await thumbnails.GetAsync(path, cancellationToken);
        if (thumbnail is null)
            return TypedResults.NotFound();

        // The disk cache is the only thumbnail cache: the browser asks again, so a regenerated thumbnail shows at once.
        context.Response.Headers[HeaderNames.CacheControl] = NoStore;
        return TypedResults.PhysicalFile(thumbnail, "image/jpeg", lastModified: File.GetLastWriteTimeUtc(thumbnail));
    }

    /// <summary>
    /// Pictures, videos, audio and plain text open in the browser; anything else is downloaded. SVG pictures are
    /// documents that run scripts when opened by themselves: they still show in &lt;img&gt;, which ignores the download.
    /// </summary>
    internal static bool OpensInline(string contentType) =>
        contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
        || contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("text/plain", StringComparison.OrdinalIgnoreCase)
        || (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && !contentType.Contains("svg", StringComparison.OrdinalIgnoreCase));

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
