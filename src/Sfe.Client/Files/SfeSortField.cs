namespace Otools.Net.Sfe.Client;

/// <summary>The order of a folder's listing (folders always come first).</summary>
public enum SfeSortField
{
    /// <summary>By name, as the server compares names.</summary>
    Name,

    /// <summary>By date of change.</summary>
    Modified,

    /// <summary>By size.</summary>
    Size,

    /// <summary>By kind, then extension.</summary>
    Type,
}
