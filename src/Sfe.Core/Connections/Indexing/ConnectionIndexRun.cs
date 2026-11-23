namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>A sync of one connection running in a library (<see cref="ConnectionIndexes"/>).</summary>
public sealed class ConnectionIndexRun(int connectionId, DateTimeOffset started)
{
    public int ConnectionId { get; } = connectionId;
    public DateTimeOffset Started { get; } = started;

    /// <summary>The connection's name; null for an account's own (not named to the library's administrators).</summary>
    public string? Name { get; internal set; }

    public ConnectionIndexProgress Progress { get; } = new();

    internal CancellationTokenSource Stop { get; } = new();

    internal Task Done { get; set; } = Task.CompletedTask;

    /// <summary>
    /// A watched folder's quick look at the changes (<see cref="ConnectionIndexes.FollowAsync"/>), not a sync: no
    /// run of the task, not shown as running; a sync asked meanwhile waits for it.
    /// </summary>
    internal bool Quiet { get; init; }

    /// <summary>"Listing “Team Dropbox”: 1,200 files" or "Reading the changes of a private connection: 12".</summary>
    public string Describe()
    {
        var what = Name is null ? "a private connection" : $"“{Name}”";
        return Progress.Listing
            ? $"Listing {what}: {Progress.Files:N0} files, {Progress.Folders:N0} folders"
            : $"Reading the changes of {what}: {Progress.Changes:N0}";
    }
}
