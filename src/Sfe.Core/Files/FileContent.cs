using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Paths;

namespace Sfe.Core.Files;

/// <summary>
/// The version of a file's content as it was read: changes when its bytes do. Not the modification time alone: FAT
/// keeps it to 2 s, and copy tools and network shares keep the old one.
/// </summary>
/// <param name="Token">Size, time and a hash of the bytes, compared as a whole (an HTTP entity tag).</param>
public sealed record FileVersion(string Token, long Size, DateTime Modified)
{
    public static FileVersion Of(ReadOnlySpan<byte> bytes, DateTime modified) =>
        new($"{bytes.Length:x}-{modified.Ticks:x}-{Convert.ToHexStringLower(SHA256.HashData(bytes))[..16]}", bytes.Length, modified);

    /// <summary>The version of the file as it is now on the disk (read whole, without holding it in memory).</summary>
    public static async Task<FileVersion> OfFileAsync(string full, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true);
        var modified = File.GetLastWriteTimeUtc(full);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken))[..16];
        return new FileVersion($"{stream.Length:x}-{modified.Ticks:x}-{hash}", stream.Length, modified);
    }
}

/// <param name="Bytes">The content; null when the file is larger than asked (<see cref="TooLarge"/>).</param>
/// <param name="Version">Null when the file was too large to be read.</param>
public sealed record FileRead(byte[]? Bytes, FileVersion? Version, long Size, DateTime Modified)
{
    public bool TooLarge => Bytes is null;
}

/// <summary>A part of a text file (<see cref="FileContent.ReadPartAsync"/>).</summary>
/// <param name="Text">The part as text; null when the file is binary.</param>
/// <param name="Offset">Where the part starts in the file, in bytes (after a cut to a whole line).</param>
/// <param name="End">Where it ends: the offset of the next part.</param>
/// <param name="Size">The file's size when it was read.</param>
/// <param name="Encoding">How it was read (as <see cref="TextCodec"/> names them; a UTF-8 mark is plain <c>utf-8</c>).</param>
public sealed record TextPart(string? Text, long Offset, long End, long Size, string Encoding, DateTime Modified);

/// <summary>Where a part of a text file read from anywhere is cut, so that it holds whole lines and whole characters.</summary>
public static class TextParts
{
    /// <summary>
    /// The bytes of <paramref name="bytes"/> to keep: from after the first line break (unless <paramref name="atStart"/>)
    /// to after the last one (unless <paramref name="atEnd"/>). Without a line break, at whole characters only (a
    /// UTF-8 sequence is never cut). <paramref name="encoding"/> null or 8-bit: one byte a character unit.
    /// </summary>
    public static (int From, int To) Cut(ReadOnlySpan<byte> bytes, bool atStart, bool atEnd, string? encoding)
    {
        ReadOnlySpan<byte> lineBreak = encoding switch
        {
            TextCodec.Utf16Le => [(byte)'\n', 0],
            TextCodec.Utf16Be => [0, (byte)'\n'],
            _ => [(byte)'\n'],
        };
        var unit = lineBreak.Length;
        var from = 0;
        var to = bytes.Length;
        if (!atStart && IndexOf(bytes, lineBreak, unit) is var first and >= 0 && first + unit < to)
            from = first + unit;
        else if (!atStart && unit == 1 && encoding != TextCodec.Windows1252)
            while (from < to && from < 3 && (bytes[from] & 0xC0) == 0x80)
                from++;
        if (!atEnd && LastIndexOf(bytes[from..], lineBreak, unit) is var last and >= 0)
            to = from + last + unit;
        else if (!atEnd && unit == 1 && encoding != TextCodec.Windows1252)
            to = from + WholeUtf8(bytes[from..to]);
        return (from, to);
    }

    /// <summary>The first line break at a character boundary (UTF-16: an even offset).</summary>
    private static int IndexOf(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> lineBreak, int unit)
    {
        for (var i = 0; i + unit <= bytes.Length; i += unit)
            if (bytes.Slice(i, unit).SequenceEqual(lineBreak))
                return i;
        return -1;
    }

    private static int LastIndexOf(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> lineBreak, int unit)
    {
        for (var i = (bytes.Length - unit) / unit * unit; i >= 0; i -= unit)
            if (bytes.Slice(i, unit).SequenceEqual(lineBreak))
                return i;
        return -1;
    }

    /// <summary>How many bytes of <paramref name="bytes"/> end at a whole UTF-8 character (an 8-bit text: all of them).</summary>
    private static int WholeUtf8(ReadOnlySpan<byte> bytes)
    {
        // Back over at most three continuation bytes to the start of the last sequence; keep it only when complete.
        var i = bytes.Length - 1;
        var back = 0;
        while (i >= 0 && back < 3 && (bytes[i] & 0xC0) == 0x80)
        {
            i--;
            back++;
        }
        if (i < 0 || bytes[i] < 0xC0)
            return bytes.Length;
        var needed = bytes[i] >= 0xF0 ? 4 : bytes[i] >= 0xE0 ? 3 : 2;
        return back + 1 >= needed ? bytes.Length : i;
    }
}

public enum OverwriteStatus
{
    Done,
    NotFound,

    /// <summary>Its content is not the version the editor started from (changed elsewhere meanwhile).</summary>
    Changed,

    /// <summary>A link (symbolic link, junction): saving would replace the link, not what it leads to.</summary>
    IsLink,

    /// <summary>Marked read-only on the disk.</summary>
    ReadOnly,

    /// <summary>A scan runs over its folder: try again once it has finished.</summary>
    ScanRunning,

    /// <summary>The disk refused (in use, no write access, the previous version couldn't be kept).</summary>
    Failed,
}

/// <param name="Version">The new version (Done), or the one on the disk now (Changed).</param>
/// <param name="TrashId">The previous version's trash entry, when this save kept one.</param>
/// <param name="Message">Why it was not done, for the person (never a server path).</param>
public sealed record OverwriteResult(OverwriteStatus Status, FileVersion? Version = null, int? TrashId = null, string? Message = null);

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
    ILogger<FileContent> logger)
{
    /// <summary>Tries of a replace refused because the file is in use (an editor or a scan reading it), and the wait between them.</summary>
    private const int InUseAttempts = 3;
    private static readonly TimeSpan InUseDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>How long an editing session keeps its "previous version already in the trash".</summary>
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

    /// <summary>One overwrite at a time (they are rare; the version check and the replace must not interleave).</summary>
    private readonly SemaphoreSlim _writing = new(1, 1);

    /// <summary>Editing sessions (account, session, file) whose previous version is in the trash already, and since when.</summary>
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
            stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true);
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
            while (read < buffer.Length && (count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken)) > 0)
                read += count;
            if (read > maxBytes)
                return new FileRead(null, null, read, modified);
            var bytes = read == buffer.Length ? buffer : buffer[..read];
            return new FileRead(bytes, FileVersion.Of(bytes, modified), read, modified);
        }
    }

    /// <summary>
    /// A part of a text file of any size (agents/docs/features/text-viewer.md, large files and logs): about
    /// <paramref name="length"/> bytes from <paramref name="offset"/>, or the last ones when it is negative, cut at whole
    /// lines (unless a line is longer than the part) and decoded in <paramref name="encoding"/>, or as the file's mark
    /// says, or as UTF-8 when the part is valid UTF-8 and else windows-1252. Null when the file is not there; a part
    /// with null text when it is binary.
    /// </summary>
    public async Task<TextPart?> ReadPartAsync(string full, long offset, int length, string? encoding, CancellationToken cancellationToken)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true);
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
            while (read < buffer.Length && (count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken)) > 0)
                read += count;
            var (from, to) = TextParts.Cut(buffer.AsSpan(0, read), atStart: lineStart, atEnd: start + read >= size, encoding ?? mark);
            var bytes = buffer.AsSpan(from, to - from);
            var name = encoding ?? (mark is TextCodec.Utf8Bom ? TextCodec.Utf8 : mark) ?? (TextCodec.IsUtf8(bytes) ? TextCodec.Utf8 : TextCodec.Windows1252);
            return new TextPart(TextCodec.Read(bytes, name), start + from, start + to, size, name, modified);
        }
    }

    /// <summary>
    /// The version of <paramref name="full"/> now, without reading it when its size and time are those of
    /// <paramref name="known"/> (a check that it was not changed elsewhere, every few seconds). Null when it is not there.
    /// </summary>
    public async Task<FileVersion?> VersionAsync(string full, string? known, CancellationToken cancellationToken)
    {
        var info = new FileInfo(full);
        if (!info.Exists)
            return null;
        if (known is not null && known.StartsWith($"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}-", StringComparison.Ordinal))
            return new FileVersion(known, info.Length, info.LastWriteTimeUtc);
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
        string relative, byte[] content, string ifMatch, string session, bool keepPrevious, FileActor actor, CancellationToken cancellationToken = default)
    {
        if (!paths.TryResolve(relative, out var full) || paths.ToRelative(full) is not { Length: > 0 } canonical || !File.Exists(full))
            return new OverwriteResult(OverwriteStatus.NotFound, Message: "The file is not there any more.");
        var info = new FileInfo(full);
        if (info.LinkTarget is not null)
            return new OverwriteResult(OverwriteStatus.IsLink, Message: "This file is a link to another one: it can't be saved here. Save a copy instead.");
        if (info.IsReadOnly)
            return new OverwriteResult(OverwriteStatus.ReadOnly, Message: "This file is marked read-only on the server: it can't be saved over. Save a copy instead.");

        await _writing.WaitAsync(cancellationToken);
        try
        {
            using var operation = scans.TryBeginOperation(full);
            if (operation is null)
                return new OverwriteResult(OverwriteStatus.ScanRunning, Message: "A scan is running over this folder: save again when it has finished.");

            if (ifMatch != "*")
            {
                var current = await FileVersion.OfFileAsync(full, cancellationToken);
                if (current.Token != ifMatch)
                    return new OverwriteResult(OverwriteStatus.Changed, current, Message: "The file was changed elsewhere since it was opened.");
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
                    logger.LogWarning(ex, "{Account} could not save {Path}: its previous version could not be kept in the trash", actor.Name, canonical);
                    return new OverwriteResult(OverwriteStatus.Failed, Message: "The file was not saved: its previous version could not be kept in the trash.");
                }
            }

            var temporary = Path.Combine(Path.GetDirectoryName(full)!, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.sfe-tmp");
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
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
                return new OverwriteResult(OverwriteStatus.Failed, Message: ex is UnauthorizedAccessException
                    ? "The file was not saved: the server may not write it."
                    : "The file was not saved: it is in use, or the disk refused. Try again in a moment.");
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
                logger.LogWarning(ex, "The index could not take the new content of {Path}: the next scan does", canonical);
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
