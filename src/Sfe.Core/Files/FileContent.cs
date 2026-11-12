using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Paths;

namespace Sfe.Core.Files;

/// <summary>
/// Reads and writes the content of the library's files for the viewers and editors of the web client
/// (agents/docs/development/file-viewers-plan.md): one place for size limits, versions and saving in place, whatever
/// the format. The routes check rights and folder rules (overwriting: the rule of move/copy's Replace).
/// </summary>
public sealed class FileContent(
    LibraryPaths paths,
    ScanCoordinator scans,
    Trash trash,
    DirectoryIndexer indexer,
    LibraryBrowser browser,
    ILogger<FileContent> logger
)
{
    /// <summary>
    /// Tries of a replace refused because the file is in use (an editor or a scan reading it), and the wait between
    /// them.
    /// </summary>
    private const int InUseAttempts = 3;
    private static readonly TimeSpan InUseDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>How long an editing session keeps its "previous version already in the trash".</summary>
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

    /// <summary>
    /// One overwrite at a time (they are rare; the version check and the replace must not interleave).
    /// </summary>
    private readonly SemaphoreSlim _writing = new(1, 1);

    /// <summary>
    /// Editing sessions (account, session, file) whose previous version is in the trash already, and since when.
    /// </summary>
    private readonly ConcurrentDictionary<(int? Account, string Session, string Path), DateTime> _kept = new();

    /// <summary>
    /// The content of <paramref name="full"/> when it is at most <paramref name="maxBytes"/> long. A file still being
    /// written (a log) is read as it is now; null when it is not there.
    /// </summary>
    public async Task<FileRead?> ReadAsync(string full, long maxBytes, CancellationToken cancellationToken)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(
                full,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                useAsync: true
            );
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        await using (stream)
        {
            var modified = File.GetLastWriteTimeUtc(full);
            var length = stream.Length;
            if (length > maxBytes)
                return new FileRead(null, null, length, modified);
            // It may grow while being read: read what fits, one byte more tells it went past the limit.
            var buffer = new byte[Math.Min(length + 1, maxBytes + 1)];
            var read = 0;
            int count;
            while (
                read < buffer.Length && (count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken)) > 0
            )
            {
                read += count;
            }

            if (read > maxBytes)
                return new FileRead(null, null, read, modified);
            var bytes = read == buffer.Length ? buffer : buffer[..read];
            return new FileRead(bytes, FileVersion.Of(bytes, modified), read, modified);
        }
    }

    /// <summary>
    /// A part of a text file of any size (agents/docs/features/text-viewer.md, large files and logs): about
    /// <paramref name="length"/> bytes from <paramref name="offset"/>, or the last ones when it is negative, cut at
    /// whole lines (unless a line is longer than the part) and decoded in <paramref name="encoding"/>, or as the file's
    /// mark says, or as UTF-8 when the part is valid UTF-8 and else windows-1252. Null when the file is not there; a
    /// part with null text when it is binary.
    /// </summary>
    public async Task<TextPart?> ReadPartAsync(
        string full,
        long offset,
        int length,
        string? encoding,
        CancellationToken cancellationToken
    )
    {
        FileStream stream;
        try
        {
            stream = new FileStream(
                full,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                useAsync: true
            );
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        await using (stream)
        {
            var modified = File.GetLastWriteTimeUtc(full);
            var size = stream.Length;
            var head = new byte[(int)Math.Min(size, TextCodec.BinaryProbe)];
            await stream.ReadExactlyAsync(head, cancellationToken);
            var (mark, markLength) = TextCodec.ByteOrderMark(head);
            var wide = (encoding ?? mark) is TextCodec.Utf16Le or TextCodec.Utf16Be;
            if (!wide && head.AsSpan(markLength).Contains((byte)0))
                return new TextPart(null, 0, 0, size, encoding ?? mark ?? TextCodec.Utf8, modified);

            var start = offset < 0 ? Math.Max(markLength, size - length) : Math.Clamp(offset, markLength, size);
            var end = Math.Min(size, start + length);
            if (wide && (start - markLength) % 2 == 1)
                start++;
            if (wide && (end - markLength) % 2 == 1)
                end--;
            // Right after a line break (the end of the part before): it starts with a whole line already.
            var unit = wide ? 2 : 1;
            var lineStart = start <= markLength;
            if (!lineStart)
            {
                var before = new byte[unit];
                stream.Position = start - unit;
                await stream.ReadExactlyAsync(before, cancellationToken);
                lineStart = (encoding ?? mark) switch
                {
                    TextCodec.Utf16Le => before is [(byte)'\n', 0],
                    TextCodec.Utf16Be => before is [0, (byte)'\n'],
                    _ => before is [(byte)'\n'],
                };
            }
            var buffer = new byte[Math.Max(0, end - start)];
            stream.Position = start;
            var read = 0;
            int count;
            while (
                read < buffer.Length && (count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken)) > 0
            )
            {
                read += count;
            }

            var (from, to) = TextParts.Cut(
                buffer.AsSpan(0, read),
                atStart: lineStart,
                atEnd: start + read >= size,
                encoding ?? mark
            );
            var bytes = buffer.AsSpan(from, to - from);
            var name = encoding
                ?? (mark is TextCodec.Utf8Bom ? TextCodec.Utf8 : mark)
                ?? (TextCodec.IsUtf8(bytes) ? TextCodec.Utf8 : TextCodec.Windows1252);
            return new TextPart(TextCodec.Read(bytes, name), start + from, start + to, size, name, modified);
        }
    }

    /// <summary>
    /// The version of <paramref name="full"/> now, without reading it when its size and time are those of
    /// <paramref name="known"/> (a check that it was not changed elsewhere, every few seconds). Null when it is not
    /// there.
    /// </summary>
    public async Task<FileVersion?> VersionAsync(string full, string? known, CancellationToken cancellationToken)
    {
        var info = new FileInfo(full);
        if (!info.Exists)
            return null;
        if (
            known is not null
            && known.StartsWith($"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}-", StringComparison.Ordinal)
        )
        {
            return new FileVersion(known, info.Length, info.LastWriteTimeUtc);
        }

        try
        {
            return await FileVersion.OfFileAsync(full, cancellationToken);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Saves <paramref name="content"/> over the file <paramref name="relative"/>, as a text editor of the OS does, but
    /// safely: only over the version <paramref name="ifMatch"/> (<c>*</c>: whatever is there), the previous version
    /// kept in the trash once per editing <paramref name="session"/> (unless <paramref name="keepPrevious"/> is off: a
    /// space that deletes for good), the new content written beside it and swapped in whole (never half a file), its
    /// index row and the listings refreshed. The file keeps its tags, attributes and permissions.
    /// </summary>
    public async Task<OverwriteResult> OverwriteAsync(
        string relative,
        byte[] content,
        string ifMatch,
        string session,
        bool keepPrevious,
        FileActor actor,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !paths.TryResolve(relative, out var full)
            || paths.ToRelative(full) is not { Length: > 0 } canonical
            || !File.Exists(full)
        )
        {
            return new OverwriteResult(OverwriteStatus.NotFound, Message: "The file is not there any more.");
        }

        var info = new FileInfo(full);
        if (info.LinkTarget is not null)
        {
            return new OverwriteResult(
                OverwriteStatus.IsLink,
                Message: "This file is a link to another one: it can't be saved here. Save a copy instead."
            );
        }

        if (info.IsReadOnly)
        {
            return new OverwriteResult(
                OverwriteStatus.ReadOnly,
                Message: "This file is marked read-only on the server: it can't be saved over. Save a copy instead."
            );
        }

        await _writing.WaitAsync(cancellationToken);
        try
        {
            using var operation = scans.TryBeginOperation(full);
            if (operation is null)
            {
                return new OverwriteResult(
                    OverwriteStatus.ScanRunning,
                    Message: "A scan is running over this folder: save again when it has finished."
                );
            }

            if (ifMatch != "*")
            {
                var current = await FileVersion.OfFileAsync(full, cancellationToken);
                if (current.Token != ifMatch)
                {
                    return new OverwriteResult(
                        OverwriteStatus.Changed,
                        current,
                        Message: "The file was changed elsewhere since it was opened."
                    );
                }
            }

            ForgetOldSessions();
            var key = (actor.AccountId, session, canonical.ToLowerInvariant());
            int? trashId = null;
            if (keepPrevious && !_kept.ContainsKey(key))
            {
                try
                {
                    trashId = await trash.CopyInAsync(full, canonical, actor, cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(
                        ex,
                        "{Account} could not save {Path}: its previous version could not be kept in the trash",
                        actor.Name,
                        canonical
                    );
                    return new OverwriteResult(
                        OverwriteStatus.Failed,
                        Message: "The file was not saved: its previous version could not be kept in the trash."
                    );
                }
            }

            var temporary = Path.Combine(
                Path.GetDirectoryName(full)!,
                $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.sfe-tmp"
            );
            try
            {
                await using (
                    var stream = new FileStream(
                        temporary,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        64 * 1024,
                        useAsync: true
                    )
                )
                {
                    await stream.WriteAsync(content, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    stream.Flush(flushToDisk: true);
                }
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temporary, File.GetUnixFileMode(full));
                await ReplaceAsync(temporary, full);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryDelete(temporary);
                if (trashId is { } id)
                    await ForgetPreviousAsync(id);
                logger.LogWarning(ex, "{Account} could not save {Path}", actor.Name, canonical);
                return new OverwriteResult(
                    OverwriteStatus.Failed,
                    Message: ex is UnauthorizedAccessException
                        ? "The file was not saved: the server may not write it."
                        : "The file was not saved: it is in use, or the disk refused. Try again in a moment."
                );
            }
            if (keepPrevious)
                _kept[key] = DateTime.UtcNow;

            // Saved: what follows can't undo it.
            try
            {
                await indexer.RefreshFileAsync(full, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "The index could not take the new content of {Path}: the next scan does",
                    canonical
                );
            }
            browser.Invalidate(Path.GetDirectoryName(full)!);
            logger.LogInformation("{Account} saved {Path}", actor.Name, canonical);
            var saved = File.GetLastWriteTimeUtc(full);
            return new OverwriteResult(OverwriteStatus.Done, FileVersion.Of(content, saved), trashId);
        }
        finally
        {
            _writing.Release();
        }
    }

    /// <summary>The new content in place of the old, at once (retried while the file is in use).</summary>
    private static async Task ReplaceAsync(string temporary, string full)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // Windows keeps the file's creation time, attributes and permissions; elsewhere a rename over it.
                File.Replace(temporary, full, destinationBackupFileName: null, ignoreMetadataErrors: true);
                return;
            }
            catch (IOException) when (attempt < InUseAttempts && File.Exists(temporary))
            {
                await Task.Delay(InUseDelay);
            }
        }
    }

    private async Task ForgetPreviousAsync(int trashId)
    {
        try
        {
            await trash.PurgeAsync(trashId, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not take back the previous version kept in the trash ({Id})", trashId);
        }
    }

    private void ForgetOldSessions()
    {
        var limit = DateTime.UtcNow - SessionLifetime;
        foreach (var (key, since) in _kept)
        {
            if (since < limit)
                _kept.TryRemove(key, out _);
        }
    }

    private void TryDelete(string file)
    {
        try
        {
            if (File.Exists(file))
                File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not delete the temporary file {File}", file);
        }
    }
}
