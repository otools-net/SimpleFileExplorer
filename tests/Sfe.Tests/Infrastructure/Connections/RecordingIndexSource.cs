using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Connections.Indexing;

namespace Otools.Net.Sfe.Tests.Infrastructure;

/// <summary>
/// A sync's calls straight to a storage (no gate), each recorded as <c>list path @cursor</c> or
/// <c>changes @cursor</c>; <see cref="FailAfter"/> makes the server "stop" after that many listing pages.
/// </summary>
public sealed class RecordingIndexSource(IRemoteStorage storage) : IRemoteIndexSource
{
    public List<string> Calls { get; } = [];

    /// <summary>Listing pages answered before the next one throws (a server stopping mid-listing).</summary>
    public int? FailAfter { get; set; }

    public Task<RemotePage> ListAsync(string path, string? cursor, CancellationToken cancellationToken)
    {
        if (FailAfter is { } left && Calls.Count(c => c.StartsWith("list ", StringComparison.Ordinal)) >= left)
            throw new OperationCanceledException("The server stops.");
        Calls.Add($"list {path} @{cursor}");
        return storage.ListAsync(path, cursor, cancellationToken);
    }

    public Task<RemoteChanges> ChangesAsync(string? cursor, CancellationToken cancellationToken)
    {
        Calls.Add($"changes @{cursor}");
        return storage.ChangesAsync(cursor, cancellationToken);
    }
}
