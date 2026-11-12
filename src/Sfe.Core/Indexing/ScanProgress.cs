using Sfe.Core.FileTracking;

namespace Sfe.Core.Indexing;

/// <summary>Thread-safe counters updated while a scan runs.</summary>
public sealed class ScanProgress
{
    private int _directories;
    private int _queued;
    private int _files;
    private int _removed;
    private int _errors;

    public string? CurrentDirectory { get; set; }
    public int Directories => _directories;

    /// <summary>
    /// Folders found but not scanned yet. The total is unknown up front: folders are discovered level by level.
    /// </summary>
    public int Queued => _queued;
    public int Files => _files;
    public int Removed => _removed;
    public int Errors => _errors;

    /// <summary>Missing files relinked after the scan; null until that pass ran.</summary>
    public RelinkPassResult? Relink { get; set; }

    public void AddDirectory() => Interlocked.Increment(ref _directories);

    public void AddQueued(int count) => Interlocked.Add(ref _queued, count);

    public void AddFile() => Interlocked.Increment(ref _files);

    public void AddRemoved(int count) => Interlocked.Add(ref _removed, count);

    public void AddError() => Interlocked.Increment(ref _errors);
}
