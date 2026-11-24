namespace Otools.Net.Sfe.Core.Files;

/// <summary>What to do when the target folder already holds an item of the same name.</summary>
public enum ConflictChoice
{
    /// <summary>Wait for the person's answer (<see cref="JobBoard.Answer"/>).</summary>
    Ask,

    /// <summary>The item there goes to the trash (needs the right to delete there), this one takes its place.</summary>
    Replace,

    Skip,

    /// <summary>This one gets a free name: <c>name (2).ext</c>.</summary>
    KeepBoth,

    /// <summary>
    /// A folder meeting a folder: what it holds goes into the one there, each name they share asked in turn (a file
    /// replaced, skipped or kept both; a folder merged again). Only offered between two folders
    /// (<see cref="FileJobConflict.CanMerge"/>); in a request it answers those clashes and the others are asked.
    /// </summary>
    Merge,
}
