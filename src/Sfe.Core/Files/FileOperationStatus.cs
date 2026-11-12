namespace Sfe.Core.Files;

public enum FileOperationStatus
{
    Done,

    /// <summary>The file or folder (or the folder to create one in) is not in the library.</summary>
    NotFound,

    /// <summary>The name can't be used (<see cref="FileNames.Check"/>, reserved, too long for the index).</summary>
    Invalid,

    /// <summary>
    /// Something of that name is already there (seen or not by the asker: they are told nothing else).
    /// </summary>
    Exists,

    /// <summary>A scan runs over that place: try again once it has finished.</summary>
    ScanRunning,

    /// <summary>Something else keeps it from being done now (folder rules left where the folder would go).</summary>
    Conflict,

    /// <summary>The disk refused: in use, or the server's account may not write there.</summary>
    Failed,

    /// <summary>Left as it was (a move to where it is, a name clash answered "skip").</summary>
    Skipped,

    /// <summary>The caller may not do it there (refused by the routes).</summary>
    Forbidden,

    /// <summary>
    /// It can't go to the trash (a folder holding links, on another drive than the trash): delete it permanently
    /// instead?
    /// </summary>
    TrashUnavailable,

    /// <summary>An archive protected by a password that wasn't given, or a wrong one: nothing was extracted.</summary>
    NeedsPassword,
}
