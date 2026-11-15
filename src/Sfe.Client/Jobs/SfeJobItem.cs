namespace Otools.Net.Sfe.Client;

/// <summary>What became of one item of a job.</summary>
public sealed class SfeJobItem
{
    /// <summary>The item's path before the job.</summary>
    public string Path { get; init; } = "";

    /// <summary>Its path now (moved, copied, or renamed by Keep both); null when nothing was made.</summary>
    public string? NewPath { get; init; }

    /// <summary><see cref="SfeItemStatus.Done"/>, or why not.</summary>
    public SfeItemStatus Status { get; init; }

    /// <summary>The reason, for people, when it wasn't done.</summary>
    public string? Message { get; init; }
}
