namespace Otools.Net.Sfe.Core.Files;

public enum OverwriteStatus
{
    Done,
    NotFound,

    /// <summary>Its content is not the version the editor started from (changed elsewhere meanwhile).</summary>
    Changed,

    /// <summary>A link (symbolic link, junction): saving would replace the link, not what it leads to.</summary>
    IsLink,

    /// <summary>Marked read-only on the disk.</summary>
    ReadOnly,

    /// <summary>A scan runs over its folder: try again once it has finished.</summary>
    ScanRunning,

    /// <summary>The disk refused (in use, no write access, the previous version couldn't be kept).</summary>
    Failed,
}
