namespace Otools.Net.Sfe.Client;

/// <summary>What became of one item of a delete or a job.</summary>
public enum SfeItemStatus
{
    /// <summary>A status this version of the library doesn't know (a newer server's).</summary>
    Unknown,

    /// <summary>Done.</summary>
    Done,

    /// <summary>Not there, or not seen by the caller.</summary>
    NotFound,

    /// <summary>A name or path that can't be used.</summary>
    Invalid,

    /// <summary>Something of that name is there.</summary>
    Exists,

    /// <summary>A scan runs there: try again once it ends.</summary>
    ScanRunning,

    /// <summary>It conflicts with the state of things (folder rules…).</summary>
    Conflict,

    /// <summary>The disk refused (in use, no write access).</summary>
    Failed,

    /// <summary>Left as it was (a name clash answered with Skip).</summary>
    Skipped,

    /// <summary>The caller may not.</summary>
    Forbidden,

    /// <summary>A folder with links on another drive than the trash: delete it permanently instead.</summary>
    TrashUnavailable,

    /// <summary>The archive needs a password.</summary>
    NeedsPassword,
}
