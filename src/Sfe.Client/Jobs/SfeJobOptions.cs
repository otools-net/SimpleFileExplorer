namespace Otools.Net.Sfe.Client;

/// <summary>How a move or copy runs.</summary>
public sealed class SfeJobOptions
{
    /// <summary>
    /// What to do when the target holds an item of the same name; <see cref="SfeConflictChoice.Ask"/> (the default)
    /// makes the job wait for an answer.
    /// </summary>
    public SfeConflictChoice Conflicts { get; set; } = SfeConflictChoice.Ask;

    /// <summary>
    /// A move: whether each group keeps the access it has now to a moved folder, rather than taking the target's
    /// rules (needs the right to manage folder rules).
    /// </summary>
    public bool KeepAccess { get; set; }

    /// <summary>A copy: whether the copies get the tags of their originals.</summary>
    public bool KeepTags { get; set; }
}
