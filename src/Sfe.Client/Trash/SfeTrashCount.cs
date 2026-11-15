namespace Otools.Net.Sfe.Client;

/// <summary>How many items of the trash the caller sees, counted up to the server's limit.</summary>
public sealed class SfeTrashCount
{
    /// <summary>The items counted.</summary>
    public int Count { get; init; }

    /// <summary>Whether there are more than <see cref="Count"/> (the count stopped at the limit).</summary>
    public bool More { get; init; }
}
