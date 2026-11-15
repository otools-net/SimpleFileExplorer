using System.Collections.Generic;

namespace Otools.Net.Sfe.Client.Archives;

// Request and answer of POST files/archive.

/// <summary>The body of <c>POST files/archive</c>.</summary>
internal sealed record ArchiveRequest(IReadOnlyList<string> Paths);

/// <summary>The answer of <c>POST files/archive</c>: a one-time link, relative to the server's address.</summary>
internal sealed class ArchiveLink
{
    public string Url { get; init; } = "";
    public string FileName { get; init; } = "";
}
