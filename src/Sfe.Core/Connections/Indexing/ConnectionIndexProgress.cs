namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>What a running sync has done so far (read by the task list while it runs).</summary>
public sealed class ConnectionIndexProgress
{
    private long _folders;
    private long _files;
    private long _changes;
    private long _tooLong;

    /// <summary>True while a full listing goes; false while the changes are read.</summary>
    public bool Listing { get; set; }

    /// <summary>Folders listed to their end.</summary>
    public long Folders => Interlocked.Read(ref _folders);

    /// <summary>Files met by the listing.</summary>
    public long Files => Interlocked.Read(ref _files);

    /// <summary>Changes read from the provider's cursor.</summary>
    public long Changes => Interlocked.Read(ref _changes);

    /// <summary>Items left out: their path is longer than the index stores.</summary>
    public long TooLong => Interlocked.Read(ref _tooLong);

    internal void Listed(int folders, int files)
    {
        Interlocked.Add(ref _folders, folders);
        Interlocked.Add(ref _files, files);
    }

    internal void Changed(int changes) => Interlocked.Add(ref _changes, changes);

    internal void LeftOut(int items) => Interlocked.Add(ref _tooLong, items);
}
