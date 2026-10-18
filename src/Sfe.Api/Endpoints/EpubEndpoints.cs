using System.IO.Compression;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Files;
using Sfe.Core.Library;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

/// <summary>
/// The files inside an EPUB book, for the reader of the web client (agents/docs/features/epub-reader.md): served by
/// the app itself, so that the book's chapters, styles, pictures and fonts load under the app's own policy (nothing
/// of the book runs).
/// </summary>
public static class EpubEndpoints
{
    public static RouteGroupBuilder MapEpubEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/files/epub/{book}/{**entry}", GetEntry)
            .RequireRight(Rights.ViewFiles)
            .FolderScoped("files.view at the book")
            .WithName("GetEpubEntry")
            .WithTags("Files")
            .WithSummary("A file inside an EPUB book (a chapter, a style, a picture, a font), for the reader.")
            .WithDescription($"""
                `book` is the book's path, UTF-8 in base64url, so that the links inside the book (`../images/cover.jpg`)
                resolve under it. `entry` is the file's name in the book's zip (any case when it isn't found as written);
                404 for one that isn't there, a folder, or one larger than {EpubEntries.MaxEntryBytes / (1024 * 1024)} MB.
                Read from the zip as asked, never unpacked; sandboxed as the library's files are.
                """);
        return api;
    }

    private static async Task<Results<FileStreamHttpResult, NotFound, ForbidHttpResult, ProblemHttpResult>> GetEntry(
        LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, CurrentAccess current, HttpContext context, string book, string? entry)
    {
        var state = await current.GetAsync();
        if (EpubEntries.PathOf(book) is not { } path || !paths.TryResolve(path, out var full) || paths.ToRelative(full) is not { Length: > 0 } canonical
            || !full.EndsWith(".epub", StringComparison.OrdinalIgnoreCase) || !File.Exists(full) || FileEndpoints.Protected(full, serverFiles, work))
            return TypedResults.NotFound();
        if (state.Check(paths, canonical, Rights.ViewFiles) is var verdict and not PathVerdict.Allowed)
            return verdict == PathVerdict.NotFound ? TypedResults.NotFound() : TypedResults.Forbid();

        ZipArchive archive;
        try
        {
            var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true);
            try
            {
                archive = new ZipArchive(stream, ZipArchiveMode.Read);
            }
            catch
            {
                await stream.DisposeAsync();
                throw;
            }
        }
        catch (InvalidDataException)
        {
            return TypedResults.NotFound();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return TypedResults.Problem("The book can't be read now (in use, or the server may not read it).", statusCode: StatusCodes.Status500InternalServerError);
        }
        if (EpubEntries.Find(archive, entry ?? string.Empty) is not { } found)
        {
            archive.Dispose();
            return TypedResults.NotFound();
        }
        context.Response.RegisterForDispose(archive);

        var info = new FileInfo(full);
        context.Response.Headers[HeaderNames.ContentSecurityPolicy] = SecurityHeaders.FilePolicy;
        // Asked again each time (a book replaced shows as it is now), answered 304 while the book is the same.
        context.Response.Headers[HeaderNames.CacheControl] = "private, no-cache";
        return TypedResults.Stream(found.Open(), EpubEntries.ContentTypeOf(found.FullName), lastModified: info.LastWriteTimeUtc,
            entityTag: new EntityTagHeaderValue($"\"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}\""));
    }
}
