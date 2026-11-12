namespace Sfe.Core.Files;

/// <summary>What the person may do with a folder or file put in a zip.</summary>
public enum ArchiveVerdict
{
    /// <summary>Seen and downloadable.</summary>
    Allowed,

    /// <summary>Seen, but its files may not be opened or downloaded: left out, and said so.</summary>
    Refused,

    /// <summary>Not seen at all (folder rules): left out without a word.</summary>
    Hidden,
}
