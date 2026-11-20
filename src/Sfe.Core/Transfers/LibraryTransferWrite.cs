using Otools.Net.Sfe.Core.Files;
using static Otools.Net.Sfe.Core.Transfers.LibraryTransferErrors;

namespace Otools.Net.Sfe.Core.Transfers;

/// <summary>
/// A file of a transfer written into the library: into its partial file (<c>name.sfe-partial</c>) part after part,
/// renamed to its name at commit and given its date; the partial file goes when it is dropped. Holds a scan turn over
/// where it goes until then, as the library's copies do.
/// </summary>
internal sealed class LibraryTransferWrite(
    LibraryTransferEndpoint side,
    FileStream partial,
    IDisposable scanHold,
    string destination,
    DateTime? modified
) : ITransferWrite
{
    /// <summary>Bytes written between two reads of the free space.</summary>
    private const long FreeSpaceStep = 64L << 20;

    private long _unchecked;
    private bool _ended;

    public long Offset => partial.Position;

    /// <summary>How far it is, told to open listings; ended with the write.</summary>
    public ItemWrite? Progress { get; init; }

    public async Task WriteAsync(ReadOnlyMemory<byte> part, CancellationToken cancellationToken)
    {
        CheckRoom(part.Length);
        try
        {
            await partial.WriteAsync(part, cancellationToken);
            Progress?.Add(part.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw FromDisk(ex, $"“{Path.GetFileName(destination)}” could not be written");
        }
    }

    public async Task<TransferEntry> CommitAsync(CancellationToken cancellationToken)
    {
        await partial.DisposeAsync();
        var name = Path.GetFileName(destination);
        if (File.Exists(destination) || Directory.Exists(destination))
            throw Exists(name);
        try
        {
            Place();
        }
        catch (IOException) when (File.Exists(destination) || Directory.Exists(destination))
        {
            throw Exists(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw FromDisk(ex, $"“{name}” could not be saved");
        }

        End();
        return side.Committed(destination);
    }

    public async Task AbortAsync(CancellationToken cancellationToken) => await DisposeAsync();

    public async ValueTask DisposeAsync()
    {
        if (_ended)
            return;
        await partial.DisposeAsync();
        try
        {
            File.Delete(partial.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next extraction in that folder, which removes the partial files nobody writes into.
        }
        End();
    }

    /// <summary>The partial file takes its name (never over another file) and the date it had at the source.</summary>
    private void Place()
    {
        File.Move(partial.Name, destination, overwrite: false);
        if (modified is { } date)
            File.SetLastWriteTimeUtc(destination, DateTime.SpecifyKind(date, DateTimeKind.Utc));
    }

    /// <summary>The free space, read before a large part and every 64 MB of small ones.</summary>
    private void CheckRoom(int bytes)
    {
        _unchecked += bytes;
        if (_unchecked < FreeSpaceStep && bytes < FreeSpaceStep)
            return;
        _unchecked = 0;
        if (side.RoomLeft(partial.Name) is { } room && room < bytes)
            throw NoRoom(side.MarginBytes);
    }

    private void End()
    {
        _ended = true;
        Progress?.Dispose();
        scanHold.Dispose();
    }
}
