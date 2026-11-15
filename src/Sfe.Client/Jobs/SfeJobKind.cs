namespace Otools.Net.Sfe.Client;

/// <summary>What a job does.</summary>
public enum SfeJobKind
{
    /// <summary>A kind this version of the library doesn't know (a newer server's).</summary>
    Unknown,

    /// <summary>Moves files and folders.</summary>
    Move,

    /// <summary>Copies files and folders.</summary>
    Copy,

    /// <summary>Extracts an archive.</summary>
    Extract,

    /// <summary>Compresses files and folders into a zip.</summary>
    Compress,
}
