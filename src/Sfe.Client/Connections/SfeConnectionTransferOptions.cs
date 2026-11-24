namespace Otools.Net.Sfe.Client;

/// <summary>How a move or copy inside a connection runs (done by the provider at once: no job).</summary>
public sealed class SfeConnectionTransferOptions
{
    /// <summary>
    /// What to do when the target holds an item of the same name: <see cref="SfeConflictChoice.Ask"/> (the default)
    /// and <see cref="SfeConflictChoice.Skip"/> leave it (its result <see cref="SfeItemStatus.Exists"/>: nothing waits
    /// for an answer), <see cref="SfeConflictChoice.Replace"/> replaces a file (never a folder),
    /// <see cref="SfeConflictChoice.KeepBoth"/> lets the provider number the name;
    /// <see cref="SfeConflictChoice.Merge"/> leaves it as <c>Ask</c> does (a provider's own moves don't merge folders).
    /// </summary>
    public SfeConflictChoice Conflicts { get; set; } = SfeConflictChoice.Ask;

    /// <summary>
    /// A move that would let more of the library's people see or change a folder (by the library's folder rules of the
    /// connection): true keeps the access they have now, false moves it as asked; null (the default) refuses it with
    /// an <see cref="SfeConflictException"/> (<c>access-gains</c>) and moves nothing.
    /// </summary>
    public bool? KeepAccess { get; set; }
}
