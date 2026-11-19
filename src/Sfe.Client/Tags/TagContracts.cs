using System.Collections.Generic;

namespace Otools.Net.Sfe.Client.Tags;

// Requests and answers of the tag routes (tags, file-tags), the parts the facade uses.

/// <summary>The body of <c>POST file-tags</c>.</summary>
internal sealed record FileTagChange(IReadOnlyList<string> Paths, IReadOnlyList<int> Add, IReadOnlyList<int> Remove);

/// <summary>The answer of <c>POST file-tags</c>.</summary>
internal sealed class FileTagChangeAnswer
{
    public int Added { get; init; }
    public int Removed { get; init; }
    public List<SfeFileTags> Files { get; init; } = [];
}

/// <summary>The body of <c>POST file-tags/scope</c>.</summary>
internal sealed record ScopeTagChange(
    string Path,
    bool Recursive,
    string? Query,
    IReadOnlyList<int> Add,
    IReadOnlyList<int> Remove
);
