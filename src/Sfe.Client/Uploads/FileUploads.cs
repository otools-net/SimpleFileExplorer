using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Otools.Net.Sfe.Client.Http;

namespace Otools.Net.Sfe.Client.Uploads;

/// <summary>
/// Resumable uploads (agents/docs/features/uploads.md) into the library or a connection (the same routes below
/// <paramref name="place"/>): the name checked, the upload started (or an unfinished one found again), its bytes sent
/// in chunks of at most <paramref name="chunkBytes"/>, then finished into its folder.
/// </summary>
internal sealed class FileUploads(SfeTransport transport, FilePlace place, int chunkBytes)
{
    /// <summary>
    /// Within this, the date of an unfinished upload is the same as the one given (as the web page does).
    /// </summary>
    private static readonly TimeSpan SameDate = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Uploads <paramref name="content"/> as <paramref name="name"/> (a path below the folder for a folder upload)
    /// into <paramref name="target"/>; answers the new file, or null when skipped.
    /// </summary>
    public async Task<SfeItem?> UploadAsync(
        string target,
        string name,
        Stream content,
        SfeUploadOptions options,
        CancellationToken cancellationToken
    )
    {
        var size = SizeOf(content, options);
        if (options.Conflicts == SfeConflictChoice.Ask)
            await RefuseTakenAsync(target, name, cancellationToken).ConfigureAwait(false);
        var origin = content.CanSeek ? content.Position : 0;
        var start = new UploadStart(target, name, size, options.Modified);
        var upload = await FindOrStartAsync(start, options.Resume && content.CanSeek, cancellationToken)
            .ConfigureAwait(false);
        if (upload.Received > 0)
            content.Position = origin + upload.Received;
        try
        {
            var chunks = new UploadChunks(transport, place, upload, chunkBytes);
            await chunks.SendAsync(content, origin, options.Progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await CancelQuietlyAsync(upload.Id).ConfigureAwait(false);
            throw;
        }
        return await FinishAsync(upload.Id, options.Conflicts, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Uploads every file below <paramref name="localFolder"/> into a folder of its name in
    /// <paramref name="target"/>, its folders made on the way; answers the files made (not those skipped).
    /// </summary>
    public async Task<IReadOnlyList<SfeItem>> UploadFolderAsync(
        string localFolder,
        string target,
        SfeUploadOptions options,
        CancellationToken cancellationToken
    )
    {
        var root = Path.GetFullPath(localFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folderName = Path.GetFileName(root);
        var items = new List<SfeItem>();
        long before = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var name = SfePath.Combine(folderName, RelativeName(root, file));
            using var stream = File.OpenRead(file);
            var progress = options.Progress is { } total ? new OffsetProgress(total, before) : null;
            var fileOptions = options.ForFile(stream.Length, File.GetLastWriteTimeUtc(file), progress);
            if (
                await UploadAsync(target, name, stream, fileOptions, cancellationToken).ConfigureAwait(false) is
                { } item
            )
                items.Add(item);
            before += stream.Length;
        }
        return items;
    }

    private static long SizeOf(Stream content, SfeUploadOptions options)
    {
        if (options.Size is { } size)
            return size;
        if (!content.CanSeek)
            throw new ArgumentException("The size of a stream that can't seek must be given (SfeUploadOptions.Size).");
        return content.Length - content.Position;
    }

    /// <summary>
    /// Fails as the finish would (409 <c>file-exists</c>) when the name is taken, before sending anything.
    /// </summary>
    private async Task RefuseTakenAsync(string target, string name, CancellationToken cancellationToken)
    {
        var request = new SfeRequest(HttpMethod.Post, place.Route("files/uploads/check"))
        {
            Content = () => SfeJson.Content(new UploadCheckRequest(target, [name])),
            // A question: asking it twice changes nothing.
            CanRetry = true,
        };
        var check = await transport.ReadAsync<UploadCheck>(request, cancellationToken).ConfigureAwait(false);
        if (check.Existing.Count == 0)
            return;
        var problem = new SfeProblem
        {
            Type = "file-exists",
            Detail = $"\"{SfePath.Combine(target, name)}\" is already there.",
        };
        throw new SfeConflictException(409, problem);
    }

    /// <summary>The unfinished upload of this file when there is one to go on with, else a new one.</summary>
    private async Task<UploadInfo> FindOrStartAsync(UploadStart start, bool resume, CancellationToken cancellationToken)
    {
        if (resume && await FindUnfinishedAsync(start, cancellationToken).ConfigureAwait(false) is { } unfinished)
            return unfinished;
        return await transport
            .ReadAsync<UploadInfo>(SfeRequest.Post(place.Route("files/uploads"), start), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<UploadInfo?> FindUnfinishedAsync(UploadStart start, CancellationToken cancellationToken)
    {
        var unfinished = await transport
            .ReadAsync<List<UploadInfo>>(SfeRequest.Get(place.Route("files/uploads")), cancellationToken)
            .ConfigureAwait(false);
        return unfinished.FirstOrDefault(upload => IsSameFile(upload, start));
    }

    private static bool IsSameFile(UploadInfo upload, UploadStart start)
    {
        var sameDate = (upload.Modified, start.Modified) switch
        {
            (null, null) => true,
            ({ } kept, { } given) => (kept - given).Duration() <= SameDate,
            _ => false,
        };
        var samePlace = SfePath.Normalize(upload.Target) == start.Target && upload.Name == start.Name;
        return samePlace && upload.Size == start.Size && sameDate;
    }

    private async Task<SfeItem?> FinishAsync(
        string id,
        SfeConflictChoice conflicts,
        CancellationToken cancellationToken
    )
    {
        var request = SfeRequest.Post(place.Route($"files/uploads/{id}/finish"), new UploadFinish(conflicts));
        using var response = await transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NoContent)
            return null;
        return await SfeJson.ReadAsync<SfeItem>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes an upload the caller stopped: best effort, the server drops abandoned ones anyway.</summary>
    private async Task CancelQuietlyAsync(string id)
    {
        try
        {
            // Not the caller's token: it is cancelled already, and the removal must still be sent.
            var request = SfeRequest.Delete(place.Route($"files/uploads/{id}"));
            await transport.RunAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SfeException or HttpRequestException or TimeoutException)
        {
            // Gone already, or the server unreachable: it removes unfinished uploads after a while.
        }
    }

    /// <summary>The path of <paramref name="file"/> below <paramref name="root"/>, with <c>/</c>.</summary>
    private static string RelativeName(string root, string file) =>
        file.Substring(root.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
}
