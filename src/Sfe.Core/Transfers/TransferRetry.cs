using Otools.Net.Sfe.Core.Files;

namespace Otools.Net.Sfe.Core.Transfers;

/// <summary>
/// What a transfer job that ended with failed items keeps for <see cref="TransferRetries.Kept"/> to start them again
/// (<see cref="TransferJobs.Retry"/>): its places, conflict choice and the asked items that did not go. A file whose
/// writing broke is written again from its start.
/// </summary>
public sealed record TransferRetry
{
    public required int JobId { get; init; }

    /// <summary>The account that started the job: the only one who may start it again.</summary>
    public required int? OwnerId { get; init; }

    public required FileJobKind Kind { get; init; }

    /// <summary>The connection the items are read from; null for the library.</summary>
    public int? FromConnection { get; init; }

    public required TransferPlace To { get; init; }

    public ConflictChoice Conflicts { get; init; }

    /// <summary>The asked items that failed or were not reached (a cancel, a job that stopped).</summary>
    public required IReadOnlyList<string> Paths { get; init; }

    public required DateTimeOffset Ended { get; init; }

    /// <summary>The asked items of <paramref name="start"/> that are neither done nor left out on purpose.</summary>
    internal static TransferRetry Of(int jobId, TransferStart start, FileJobView view)
    {
        var went = view
            .Results.Where(r => r.Status is FileOperationStatus.Done or FileOperationStatus.Skipped)
            .Select(r => r.Path)
            .ToHashSet(StringComparer.Ordinal);
        return new TransferRetry
        {
            JobId = jobId,
            OwnerId = start.Actor.AccountId,
            Kind = start.Kind,
            FromConnection = start.FromConnection,
            To = start.To,
            Conflicts = start.Conflicts,
            Paths = [.. start.Paths.Distinct(StringComparer.Ordinal).Where(p => !went.Contains(p))],
            Ended = DateTimeOffset.UtcNow,
        };
    }
}
