namespace Otools.Net.Sfe.Client;

/// <summary>The name clash a job waits on: the item it brings and the one already in the target.</summary>
public sealed class SfeJobConflict
{
    /// <summary>The item the job moves or copies.</summary>
    public SfeClashSide Source { get; init; } = new();

    /// <summary>The item of the same name in the target.</summary>
    public SfeClashSide Existing { get; init; } = new();

    /// <summary>Whether the caller may replace it (<see cref="SfeConflictChoice.Replace"/>).</summary>
    public bool CanReplace { get; init; }

    /// <summary>Whether both are folders the job may merge (<see cref="SfeConflictChoice.Merge"/>).</summary>
    public bool CanMerge { get; init; }
}
