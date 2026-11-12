namespace Sfe.Core.Files;

/// <summary>An upload not finished yet: what the uploader's browser needs to go on (also after a reload).</summary>
/// <param name="Received">Bytes stored so far: the next chunk starts there.</param>
public sealed record UploadInfo(
    string Id,
    string Target,
    string Name,
    long Size,
    long Received,
    DateTimeOffset? Modified,
    int? OwnerId,
    string OwnerName,
    DateTimeOffset Started,
    DateTimeOffset LastActivity
);
