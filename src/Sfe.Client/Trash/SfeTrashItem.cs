using System;

namespace Otools.Net.Sfe.Client;

/// <summary>A file or folder in the library's trash.</summary>
public sealed class SfeTrashItem
{
    /// <summary>Its id in the trash.</summary>
    public int Id { get; init; }

    /// <summary>Where it was.</summary>
    public string Path { get; init; } = "";

    /// <summary>Its name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Whether it is a folder.</summary>
    public bool IsFolder { get; init; }

    /// <summary>Its size in bytes (a folder's files together), when known.</summary>
    public long? Size { get; init; }

    /// <summary>A folder's number of files, when known.</summary>
    public int? Files { get; init; }

    /// <summary>When it was deleted.</summary>
    public DateTimeOffset DeletedAt { get; init; }

    /// <summary>Who deleted it, when known.</summary>
    public string? DeletedBy { get; init; }
}
