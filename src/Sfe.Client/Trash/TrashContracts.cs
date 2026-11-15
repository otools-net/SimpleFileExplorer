using System.Collections.Generic;

namespace Otools.Net.Sfe.Client.Trash;

// Answers of the trash routes (files/trash).

/// <summary>A page of <c>GET files/trash</c>, newest first.</summary>
internal sealed class TrashPage
{
    public List<SfeTrashItem> Items { get; init; } = [];
    public bool HasMore { get; init; }
    public int? Next { get; init; }
}

/// <summary>The answer of <c>DELETE files/trash</c>.</summary>
internal sealed class EmptiedTrash
{
    public int Purged { get; init; }
}
