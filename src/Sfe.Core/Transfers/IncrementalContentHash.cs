namespace Sfe.Core.Transfers;

/// <summary>
/// A content hash computed while a file streams through a transfer, part by part (<see cref="ContentHashes"/>): fed
/// with <see cref="Append"/> in the file's order, read once with <see cref="Finish"/>.
/// </summary>
public abstract class IncrementalContentHash : IDisposable
{
    /// <summary>Feeds the next bytes of the file.</summary>
    public abstract void Append(ReadOnlySpan<byte> data);

    /// <summary>The hash of everything appended, in lowercase hexadecimal.</summary>
    public abstract string Finish();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected abstract void Dispose(bool disposing);
}
