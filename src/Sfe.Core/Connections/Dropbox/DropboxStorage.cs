using System.Net;
using System.Net.Http.Headers;
using Dropbox.Api;
using Dropbox.Api.Files;

namespace Sfe.Core.Connections.Dropbox;

/// <summary>
/// The files of a Dropbox account seen from a connection's root folder, through the SDK (every call through
/// <see cref="DropboxErrors.CallAsync{T}"/>). Paths are case-insensitive at Dropbox: <c>path_lower</c> compares,
/// <c>path_display</c> shows. A ranged read asks a temporary link and reads it with a <c>Range</c> header (the SDK's
/// download takes no range); the link never leaves the server. Uploads are Sequential sessions: parts of any size up
/// to <see cref="DropboxProvider"/>'s <c>MaxUploadPart</c>, in order.
/// </summary>
public sealed class DropboxStorage : IRemoteStorage
{
    /// <summary>Entries per listing page (Dropbox's own limit is 2000).</summary>
    public const uint PageSize = 500;

    private readonly DropboxClient _client;
    private readonly HttpClient _http;
    private readonly string _root;

    /// <param name="http">Made by <see cref="OutboundPolicy"/> for <see cref="DropboxProvider.Rule"/>; disposed with
    /// this.</param>
    /// <param name="rootPath">The connection's root folder (<see cref="RemotePaths"/>, no leading slash).</param>
    public DropboxStorage(DropboxClient client, HttpClient http, string rootPath)
    {
        _client = client;
        _http = http;
        _root = rootPath.Length == 0 ? "" : "/" + rootPath;
    }

    public async Task<RemotePage> ListAsync(string path, string? cursor, CancellationToken cancellationToken)
    {
        var result = await DropboxErrors.CallAsync(() =>
            cursor is null
                ? _client.Files.ListFolderAsync(new ListFolderArg(DropboxPath(path), limit: PageSize))
                : _client.Files.ListFolderContinueAsync(cursor)
        );
        return new RemotePage(
            [.. result.Entries.Where(e => !e.IsDeleted).Select(ToItem)],
            result.HasMore ? result.Cursor : null
        );
    }

    public async Task<RemoteItem?> GetAsync(string path, CancellationToken cancellationToken)
    {
        // Dropbox has no metadata for its own root, nor does a connection need it for its root.
        if (path.Length == 0)
            return new RemoteItem("", "", IsFolder: true);
        try
        {
            var metadata = await DropboxErrors.CallAsync(() => _client.Files.GetMetadataAsync(DropboxPath(path)));
            return metadata.IsDeleted ? null : ToItem(metadata);
        }
        catch (RemoteStorageException error) when (error.Kind == RemoteErrorKind.NotFound)
        {
            return null;
        }
    }

    public async Task<RemoteContent> OpenReadAsync(string path, RemoteRange? range, CancellationToken cancellationToken)
    {
        if (range is null)
        {
            var download = await DropboxErrors.CallAsync(() => _client.Files.DownloadAsync(DropboxPath(path)));
            var size = (long)download.Response.Size;
            return new RemoteContent(await download.GetContentAsStreamAsync(), size, size);
        }

        var link = await DropboxErrors.CallAsync(() => _client.Files.GetTemporaryLinkAsync(DropboxPath(path)));
        return await ReadRangeAsync(link.Link, (long)link.Metadata.Size, range, cancellationToken);
    }

    public async Task<RemoteContent?> ThumbnailAsync(string path, int size, CancellationToken cancellationToken)
    {
        try
        {
            var thumbnail = await DropboxErrors.CallAsync(() =>
                _client.Files.GetThumbnailAsync(DropboxPath(path), ThumbnailFormat.Jpeg.Instance, SizeOf(size))
            );
            var bytes = await thumbnail.GetContentAsByteArrayAsync();
            return new RemoteContent(new MemoryStream(bytes), bytes.Length, bytes.Length, ContentType: "image/jpeg");
        }
        catch (RemoteStorageException error) when (error.Kind is RemoteErrorKind.Refused or RemoteErrorKind.NotFound)
        {
            // Not a picture or a video Dropbox makes thumbnails of.
            return null;
        }
    }

    public async Task<RemoteChanges> ChangesAsync(string? cursor, CancellationToken cancellationToken)
    {
        if (cursor is null)
        {
            var latest = await DropboxErrors.CallAsync(() =>
                _client.Files.ListFolderGetLatestCursorAsync(new ListFolderArg(_root, recursive: true))
            );
            return new RemoteChanges([], latest.Cursor);
        }
        try
        {
            var result = await DropboxErrors.CallAsync(() => _client.Files.ListFolderContinueAsync(cursor));
            var changes = result.Entries.Select(e => new RemoteChange(RelativeOf(e), e.IsDeleted ? null : ToItem(e)));
            return new RemoteChanges([.. changes], result.Cursor, result.HasMore);
        }
        catch (RemoteStorageException error) when (error.Message.Contains("reset", StringComparison.Ordinal))
        {
            return new RemoteChanges([], cursor, Reset: true);
        }
    }

    public async Task<RemoteSpace?> SpaceAsync(CancellationToken cancellationToken)
    {
        var usage = await DropboxErrors.CallAsync(() => _client.Users.GetSpaceUsageAsync());
        ulong? total =
            usage.Allocation.IsIndividual ? usage.Allocation.AsIndividual.Value.Allocated
            : usage.Allocation.IsTeam ? usage.Allocation.AsTeam.Value.Allocated
            : null;
        return new RemoteSpace((long)usage.Used, total is { } t ? (long)t : null);
    }

    public async Task<string> BeginUploadAsync(CancellationToken cancellationToken)
    {
        // Sequential: the parts come in order, of any size (a concurrent session wants multiples of 4 MiB).
        var start = new UploadSessionStartArg(close: false, sessionType: UploadSessionType.Sequential.Instance);
        var started = await DropboxErrors.CallAsync(() =>
            _client.Files.UploadSessionStartAsync(start, new MemoryStream())
        );
        return started.SessionId;
    }

    public async Task AppendUploadAsync(string upload, long offset, Stream part, CancellationToken cancellationToken)
    {
        // The SDK sends the body once (no retries): a part of known length, buffered when it is a request's body.
        var body = part.CanSeek ? part : await BufferAsync(part, cancellationToken);
        try
        {
            await DropboxErrors.CallAsync(() => AppendPartAsync(upload, offset, body));
        }
        finally
        {
            if (body != part)
                await body.DisposeAsync();
        }
    }

    public async Task<RemoteItem> FinishUploadAsync(
        string upload,
        RemoteUploadTarget target,
        CancellationToken cancellationToken
    )
    {
        var commit = new CommitInfo(
            DropboxPath(target.Path),
            target.Conflict == RemoteConflict.Replace ? WriteMode.Overwrite.Instance : WriteMode.Add.Instance,
            autorename: target.Conflict == RemoteConflict.KeepBoth,
            clientModified: WholeSeconds(target.Modified)
        );
        var cursor = new UploadSessionCursor(upload, (ulong)target.Length);
        var file = await DropboxErrors.CallAsync(() =>
            _client.Files.UploadSessionFinishAsync(cursor, commit, body: new MemoryStream())
        );
        return ToItem(file);
    }

    public async Task<RemoteItem> CreateFolderAsync(string path, CancellationToken cancellationToken)
    {
        var created = await DropboxErrors.CallAsync(() =>
            _client.Files.CreateFolderV2Async(DropboxPath(path), autorename: false)
        );
        return ToItem(created.Metadata);
    }

    public Task<RemoteItem> MoveAsync(
        string from,
        string to,
        RemoteConflict conflict,
        CancellationToken cancellationToken
    ) => RelocateAsync(from, to, conflict, move: true, cancellationToken);

    public Task<RemoteItem> CopyAsync(
        string from,
        string to,
        RemoteConflict conflict,
        CancellationToken cancellationToken
    ) => RelocateAsync(from, to, conflict, move: false, cancellationToken);

    public async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        // Dropbox's root, or the connection's, is never deleted through it.
        if (path.Length == 0)
            throw new RemoteStorageException(RemoteErrorKind.NotFound, "The root folder cannot be deleted.");
        await DropboxErrors.CallAsync(() => _client.Files.DeleteV2Async(DropboxPath(path)));
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        _http.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>The Dropbox path of a path of the connection: <c>""</c> for Dropbox's root, else <c>/a/b</c>.</summary>
    public string DropboxPath(string path) => path.Length == 0 ? _root : $"{_root}/{path}";

    /// <summary>A Dropbox entry's path relative to the connection's root, as Dropbox spells it.</summary>
    public string RelativeOf(Metadata entry)
    {
        var display = entry.PathDisplay ?? entry.PathLower ?? "";
        return display.Length > _root.Length && display.StartsWith(_root + "/", StringComparison.OrdinalIgnoreCase)
            ? display[(_root.Length + 1)..]
            : display.TrimStart('/');
    }

    private RemoteItem ToItem(Metadata entry) =>
        entry.IsFile
            ? new RemoteItem(
                entry.Name,
                RelativeOf(entry),
                IsFolder: false,
                (long)entry.AsFile.Size,
                DateTime.SpecifyKind(entry.AsFile.ServerModified, DateTimeKind.Utc),
                entry.AsFile.Id,
                entry.AsFile.ContentHash,
                entry.AsFile.IsDownloadable
            )
            : new RemoteItem(entry.Name, RelativeOf(entry), IsFolder: true, Id: entry.AsFolder?.Id);

    /// <summary>Part of a file through its temporary link; the whole file when the server ignores the range.</summary>
    private async Task<RemoteContent> ReadRangeAsync(
        string link,
        long total,
        RemoteRange range,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, link);
        request.Headers.Range = new RangeHeaderValue(range.From, range.To);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception error) when (DropboxErrors.Map(error) is { } mapped)
        {
            throw mapped;
        }
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new RemoteStorageException(
                response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable
                    ? RemoteErrorKind.Refused
                    : RemoteErrorKind.Unavailable,
                $"Dropbox answered {(int)response.StatusCode} to a read."
            );
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var length = response.Content.Headers.ContentLength;
        if (response.StatusCode != HttpStatusCode.PartialContent)
            return new RemoteContent(stream, length ?? total, total);
        var served = response.Content.Headers.ContentRange is { From: { } from, To: { } to }
            ? new RemoteRange(from, to)
            : range;
        return new RemoteContent(stream, length, total, served);
    }

    /// <summary>
    /// A move or a copy. Dropbox's names are case-insensitive: a rename that changes only the case goes through a
    /// free temporary name. To replace, the file at the target is deleted first (into Dropbox's own trash); a
    /// folder there is never replaced.
    /// </summary>
    private async Task<RemoteItem> RelocateAsync(
        string from,
        string to,
        RemoteConflict conflict,
        bool move,
        CancellationToken cancellationToken
    )
    {
        if (from.Length == 0 || to.Length == 0)
            throw new RemoteStorageException(RemoteErrorKind.NotFound, "The root folder cannot be moved or copied.");
        if (move && from != to && from.Equals(to, StringComparison.OrdinalIgnoreCase))
        {
            var temporary = $"{to}.sfe-{Guid.NewGuid().ToString("N")[..8]}";
            await CallRelocationAsync(from, temporary, autorename: false, move: true);
            return await CallRelocationAsync(temporary, to, autorename: false, move: true);
        }
        if (conflict == RemoteConflict.Replace && await GetAsync(to, cancellationToken) is { } existing)
        {
            if (existing.IsFolder)
                throw new RemoteStorageException(RemoteErrorKind.Exists, "A folder of that name is already there.");
            await DeleteAsync(to, cancellationToken);
        }
        return await CallRelocationAsync(from, to, autorename: conflict == RemoteConflict.KeepBoth, move);
    }

    private async Task<RemoteItem> CallRelocationAsync(string from, string to, bool autorename, bool move)
    {
        var argument = new RelocationArg(
            DropboxPath(from),
            DropboxPath(to),
            allowSharedFolder: true,
            autorename: autorename
        );
        var result = await DropboxErrors.CallAsync(() =>
            move ? _client.Files.MoveV2Async(argument) : _client.Files.CopyV2Async(argument)
        );
        return ToItem(result.Metadata);
    }

    private static ThumbnailSize SizeOf(int size) =>
        size switch
        {
            <= 64 => ThumbnailSize.W64h64.Instance,
            <= 128 => ThumbnailSize.W128h128.Instance,
            <= 256 => ThumbnailSize.W256h256.Instance,
            <= 640 => ThumbnailSize.W640h480.Instance,
            _ => ThumbnailSize.W1024h768.Instance,
        };

    /// <summary>
    /// Sends a part; Dropbox's <c>incorrect_offset</c> as <see cref="RemoteErrorKind.WrongOffset"/> with the
    /// offset it has (a part that arrived although its answer was lost).
    /// </summary>
    private async Task<bool> AppendPartAsync(string upload, long offset, Stream body)
    {
        try
        {
            await _client.Files.UploadSessionAppendV2Async(new UploadSessionCursor(upload, (ulong)offset), body: body);
            return true;
        }
        catch (ApiException<UploadSessionAppendError> error) when (error.ErrorResponse.IsIncorrectOffset)
        {
            throw new RemoteStorageException(
                RemoteErrorKind.WrongOffset,
                "Dropbox has another part of this upload.",
                inner: error
            )
            {
                Offset = (long)error.ErrorResponse.AsIncorrectOffset.Value.CorrectOffset,
            };
        }
    }

    private static async Task<MemoryStream> BufferAsync(Stream part, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        await part.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return buffer;
    }

    /// <summary>A date as Dropbox takes it: UTC, no fraction of a second.</summary>
    private static DateTime? WholeSeconds(DateTimeOffset? date) =>
        date is { } value
            ? new DateTime(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerSecond, DateTimeKind.Utc)
            : null;
}
