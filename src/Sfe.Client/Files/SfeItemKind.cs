namespace Otools.Net.Sfe.Client;

/// <summary>What an item of the library is.</summary>
public enum SfeItemKind
{
    /// <summary>A kind this version of the library doesn't know (a newer server's).</summary>
    Unknown,

    /// <summary>A folder.</summary>
    Directory,

    /// <summary>A picture.</summary>
    Image,

    /// <summary>A video.</summary>
    Video,

    /// <summary>A sound file.</summary>
    Audio,

    /// <summary>Any other file.</summary>
    File,
}
