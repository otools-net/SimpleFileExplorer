namespace Otools.Net.Sfe.Client;

/// <summary>A folder counted on the disk: what it holds, so far or in all.</summary>
public sealed class SfeFolderCount
{
    /// <summary>Files counted.</summary>
    public long Files { get; init; }

    /// <summary>Folders counted.</summary>
    public long Folders { get; init; }

    /// <summary>Bytes of the files counted.</summary>
    public long Bytes { get; init; }

    /// <summary>Items left out: the caller may not see them, or they couldn't be read.</summary>
    public long LeftOut { get; init; }

    /// <summary>Whether the count stopped at the server's limit before the end.</summary>
    public bool Capped { get; init; }
}
