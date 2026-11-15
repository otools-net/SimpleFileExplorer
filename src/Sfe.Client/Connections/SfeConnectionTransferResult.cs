namespace Otools.Net.Sfe.Client;

/// <summary>What became of one item of a move or copy inside a connection.</summary>
public sealed class SfeConnectionTransferResult
{
    /// <summary>The item's path, as given.</summary>
    public string Path { get; init; } = "";

    /// <summary>
    /// <see cref="SfeItemStatus.Done"/>, or why not (<see cref="SfeItemStatus.Exists"/>: the name is taken and the
    /// choice was to ask or skip; <see cref="SfeItemStatus.Skipped"/>: moved where it is).
    /// </summary>
    public SfeItemStatus Status { get; init; }

    /// <summary>The reason, for people, when it wasn't done.</summary>
    public string? Message { get; init; }

    /// <summary>The item where it is now, when done.</summary>
    public SfeItem? Item { get; init; }
}
