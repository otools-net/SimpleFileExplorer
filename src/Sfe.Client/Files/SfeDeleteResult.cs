namespace Otools.Net.Sfe.Client;

/// <summary>What became of one path of a delete.</summary>
public sealed class SfeDeleteResult
{
    /// <summary>The path, as given.</summary>
    public string Path { get; init; } = "";

    /// <summary><see cref="SfeItemStatus.Done"/>, or why it wasn't deleted.</summary>
    public SfeItemStatus Status { get; init; }

    /// <summary>The reason, for people, when it wasn't deleted.</summary>
    public string? Message { get; init; }

    /// <summary>Its id in the trash (<see cref="SfeTrash.RestoreAsync"/>); null when deleted for good.</summary>
    public int? TrashId { get; init; }
}
