namespace Otools.Net.Sfe.Core.Files;

/// <summary>
/// One file or folder being written (<see cref="ItemsBeingWritten.Begin"/>): its writer counts the bytes written
/// (thread-safe, no lock); disposing it ends it.
/// </summary>
public sealed class ItemWrite : IDisposable
{
    private readonly ItemsBeingWritten _owner;
    private long _done;
    private long _told = -1;

    internal ItemWrite(ItemsBeingWritten owner, string folder, string partialName, string name, long total)
    {
        (_owner, Folder, PartialName, Name, Total) = (owner, folder, partialName, name, Math.Max(0, total));
    }

    /// <summary>The folder it is written in (root-relative).</summary>
    public string Folder { get; }

    /// <summary>Its name while written (<c>name.sfe-partial</c>).</summary>
    public string PartialName { get; }

    /// <summary>The name it takes when done.</summary>
    public string Name { get; }

    /// <summary>Its bytes; 0 when unknown.</summary>
    public long Total { get; }

    /// <summary>The bytes written so far.</summary>
    public long Done => Interlocked.Read(ref _done);

    public void Add(long bytes) => Interlocked.Add(ref _done, bytes);

    /// <summary>How far it is (an extraction: how far in the archive).</summary>
    public void Set(long bytes) => Interlocked.Exchange(ref _done, bytes);

    /// <summary>It moved since the last time this was asked (a new one has moved).</summary>
    internal bool TakeMoved()
    {
        var done = Done;
        return Interlocked.Exchange(ref _told, done) != done;
    }

    public void Dispose() => _owner.End(this);
}
