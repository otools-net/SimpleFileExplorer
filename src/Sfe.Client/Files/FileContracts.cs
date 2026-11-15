using System.Collections.Generic;

namespace Otools.Net.Sfe.Client.Files;

// Requests and answers of the item routes (browse, properties, folders, rename, delete), the parts the facade uses.

/// <summary>A page of <c>GET api/v1/browse</c>.</summary>
internal sealed class ListingPage
{
    public List<SfeItem> Items { get; init; } = [];
    public int Offset { get; init; }
    public int Total { get; init; }
}

/// <summary>The body of <c>files/folders</c> and <c>files/rename</c>: a path and a name.</summary>
internal sealed record NameRequest(string Path, string Name);

/// <summary>The body of <c>files/delete</c>.</summary>
internal sealed record DeleteRequest(IReadOnlyList<string> Paths, bool Permanent);

/// <summary>The answer of <c>files/delete</c>.</summary>
internal sealed class DeleteAnswer
{
    public List<SfeDeleteResult> Items { get; init; } = [];
}
