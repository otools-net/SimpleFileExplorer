namespace Otools.Net.Sfe.Client;

/// <summary>What to do when the target already holds an item of the same name.</summary>
public enum SfeConflictChoice
{
    /// <summary>
    /// Ask: a job waits for an answer (<see cref="SfeJobWaitOptions.OnConflict"/>); an upload fails with an
    /// <see cref="SfeConflictException"/> (<c>file-exists</c>) before sending anything.
    /// </summary>
    Ask,

    /// <summary>
    /// Replace it: the item there goes to the trash (needs the right to delete there; without it a job asks).
    /// </summary>
    Replace,

    /// <summary>Leave the item there, and this one where it is.</summary>
    Skip,

    /// <summary>Keep both: <c> (2)</c> is added to the new one's name.</summary>
    KeepBoth,

    /// <summary>
    /// A folder meeting a folder (<see cref="SfeJobConflict.CanMerge"/>): what it holds goes into the one there, each
    /// name they share asked in turn. In a job's options it answers those clashes only, the others are asked; an
    /// upload (a file) asks.
    /// </summary>
    Merge,
}
