using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Otools.Net.Sfe.Client;

/// <summary>A file or folder of the library, as listings show it.</summary>
public sealed class SfeItem
{
    /// <summary>Its name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Its path from the library's root (<see cref="SfePath"/>).</summary>
    public string Path { get; init; } = "";

    /// <summary>What it is: a folder, a picture, a video…</summary>
    public SfeItemKind Kind { get; init; }

    /// <summary>Whether it is a folder.</summary>
    [JsonIgnore]
    public bool IsFolder => Kind == SfeItemKind.Directory;

    /// <summary>Its extension with the dot (<c>.jpg</c>); null for a folder or a name without one.</summary>
    public string? Extension { get; init; }

    /// <summary>Its size in bytes; null for a folder.</summary>
    public long? Size { get; init; }

    /// <summary>When it was last changed; null when the disk doesn't say.</summary>
    public DateTimeOffset? Modified { get; init; }

    /// <summary>A picture's or video's width in pixels, when the index knows it.</summary>
    public int? Width { get; init; }

    /// <summary>A picture's or video's height in pixels, when the index knows it.</summary>
    public int? Height { get; init; }

    /// <summary>A video's or sound's length in seconds, when the index knows it.</summary>
    public double? Duration { get; init; }

    /// <summary>Whether the file is in the index (searchable, taggable); null when not told.</summary>
    public bool? Indexed { get; init; }

    /// <summary>A file's tags, when the listing tells them (folder listings and searches do).</summary>
    public IReadOnlyList<SfeTagSummary>? Tags { get; init; }
}
