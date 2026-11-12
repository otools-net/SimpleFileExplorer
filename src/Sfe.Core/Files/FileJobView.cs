namespace Sfe.Core.Files;

/// <summary>A move or copy as the operations panel shows it.</summary>
public sealed record FileJobView(
    int Id,
    FileJobKind Kind,
    FileJobState State,
    string Target,
    int Total,
    int Done,
    long BytesTotal,
    long BytesDone,
    string? Current,
    FileJobConflict? Conflict,
    IReadOnlyList<FileJobItem> Results,
    DateTimeOffset Started,
    DateTimeOffset? Finished,
    int? OwnerId
);
