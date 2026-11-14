using Sfe.Core.Connections;
using Sfe.Core.Files;

namespace Sfe.Core.Transfers;

/// <summary>
/// What became of the files and folders below one item of a transfer: the item's result is done when nothing was
/// left out, else it says the first problem and how many there were.
/// </summary>
internal sealed class TransferTally
{
    private int _problems;
    private FileOperationStatus _status = FileOperationStatus.Done;
    private string? _first;

    public void Fail(RemoteStorageException failure) => Add(TransferStatuses.Of(failure.Kind), failure.Message);

    public void LeftOut(FileJobItem item) => Add(item.Status, item.Message);

    public void Skip(string message) => Add(FileOperationStatus.Skipped, message);

    /// <summary>The item's result: <paramref name="placed"/> is where it went, when it went somewhere.</summary>
    public FileJobItem Result(string path, string? placed)
    {
        if (_problems == 0)
            return new FileJobItem(path, placed, FileOperationStatus.Done);
        var more = _problems > 1 ? $" ({_problems} items were not transferred.)" : "";
        return new FileJobItem(path, placed, _problems > 1 ? FileOperationStatus.Failed : _status, _first + more);
    }

    private void Add(FileOperationStatus status, string? message)
    {
        if (_problems++ == 0)
            (_status, _first) = (status, message);
    }
}
