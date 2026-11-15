namespace Otools.Net.Sfe.Client.Details;

// Requests and answers of files/properties/count, …/checksum and …/jobs.

/// <summary>The body of <c>files/properties/count</c>.</summary>
internal sealed record CountRequest(string Path);

/// <summary>The body of <c>files/properties/checksum</c>.</summary>
internal sealed record ChecksumRequest(string Path, SfeChecksumAlgorithm Algorithm);

/// <summary>A count or checksum running on the server (<c>PropertyJobView</c>).</summary>
internal sealed class PropertyJob
{
    public string Id { get; init; } = "";
    public bool Done { get; init; }
    public long Files { get; init; }
    public long Folders { get; init; }
    public long Bytes { get; init; }
    public long? Total { get; init; }
    public long LeftOut { get; init; }
    public bool Capped { get; init; }
    public string? Checksum { get; init; }
    public string? Error { get; init; }

    public SfeFolderCount ToCount() =>
        new()
        {
            Files = Files,
            Folders = Folders,
            Bytes = Bytes,
            LeftOut = LeftOut,
            Capped = Capped,
        };
}
