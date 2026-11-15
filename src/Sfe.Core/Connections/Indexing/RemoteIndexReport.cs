using Microsoft.Extensions.Logging;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// Tells in a sync's log what was left out of the index (a path longer than it stores): each path for a library's
/// or the server's connection, up to <see cref="MaxLines"/> a sync; for an account's own connection only the count
/// (the run's log is the library's administrators' to read, the files are the account's).
/// </summary>
internal sealed class RemoteIndexReport(ILogger logger)
{
    public const int MaxLines = 100;

    /// <summary>A page of a listing or of the changes, at Debug: how long the provider and the database took.</summary>
    public void Page(int connectionId, int entries, (TimeSpan Provider, TimeSpan Written) took) =>
        logger.LogDebug(
            "Connection {ConnectionId}: page of {Entries} entries, read in {ReadMs:N0} ms, written in {WriteMs:N0} ms",
            connectionId,
            entries,
            took.Provider.TotalMilliseconds,
            took.Written.TotalMilliseconds
        );

    public void TooLong(ConnectionIndexTarget target, IReadOnlyList<string> paths, ConnectionIndexProgress progress)
    {
        if (paths.Count == 0)
            return;
        var before = progress.TooLong;
        progress.LeftOut(paths.Count);
        if (target.Private)
            return;
        foreach (var path in paths.Take((int)Math.Max(0, MaxLines - before)))
        {
            logger.LogWarning(
                "Connection {ConnectionId}: left out of the index, its path is longer than {Max} characters: {Path}",
                target.ConnectionId,
                Data.SfeDbContext.MaxPathLength,
                path
            );
        }
    }
}
