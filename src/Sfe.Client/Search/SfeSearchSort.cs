namespace Otools.Net.Sfe.Client;

/// <summary>The order of a search's results.</summary>
public enum SfeSearchSort
{
    /// <summary>By path, so grouped by folder (the default).</summary>
    Path,

    /// <summary>By name.</summary>
    Name,

    /// <summary>By date of change.</summary>
    Modified,

    /// <summary>By size.</summary>
    Size,

    /// <summary>By extension.</summary>
    Type,

    /// <summary>By length (videos and sounds); the files whose length is unknown come last, by path.</summary>
    Duration,

    /// <summary>By width × height; the files whose size in pixels is unknown come last, by path.</summary>
    Resolution,
}
