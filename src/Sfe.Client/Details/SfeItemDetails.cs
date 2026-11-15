using System;

namespace Otools.Net.Sfe.Client;

/// <summary>What the disk tells of a file or folder: dates and attributes.</summary>
public sealed class SfeItemDetails
{
    /// <summary>When it was made, when the disk says.</summary>
    public DateTimeOffset? Created { get; init; }

    /// <summary>When it was last changed.</summary>
    public DateTimeOffset Modified { get; init; }

    /// <summary>When it was last read, when the disk says.</summary>
    public DateTimeOffset? Accessed { get; init; }

    /// <summary>Whether it is hidden.</summary>
    public bool Hidden { get; init; }

    /// <summary>Whether it is read-only.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>Whether it is a link (shortcut, junction) to another place.</summary>
    public bool IsLink { get; init; }
}
