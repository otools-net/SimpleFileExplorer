namespace Otools.Net.Sfe.Core.Files;

/// <summary>
/// What became of the items of a folder merged into another (<see cref="FileJobs"/>): the folder's one result is done
/// when nothing was left out, else it says how many were and the first one's reason.
/// </summary>
internal sealed class MergeTally(string name, string intoPath)
{
    private readonly List<FileJobItem> _leftOut = [];

    public void Add(FileJobItem item)
    {
        if (item.Status != FileOperationStatus.Done)
            _leftOut.Add(item);
    }

    /// <summary>The merged folder's result, reported under <paramref name="path"/>.</summary>
    public FileJobItem Result(string path)
    {
        if (_leftOut.Count == 0)
            return new FileJobItem(path, intoPath, FileOperationStatus.Done, Merged: true);
        var first = _leftOut[0];
        var status = _leftOut.Any(i => i.Status == FileOperationStatus.Failed)
            ? FileOperationStatus.Failed
            : first.Status;
        var count = _leftOut.Count == 1 ? "1 item was" : $"{_leftOut.Count:N0} items were";
        var reason = first.Message ?? $"“{first.Path.Split('/')[^1]}” was left out.";
        return new FileJobItem(
            path,
            intoPath,
            status,
            $"“{name}” was merged into the folder of its name there, but {count} left out. {reason}",
            Merged: true
        );
    }
}
