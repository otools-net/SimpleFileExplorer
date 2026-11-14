using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Core.Library;

/// <summary>What a folder of the library can be kept out of.</summary>
public enum ExclusionKind
{
    /// <summary>Scans (<see cref="ScanOptions.ExcludedFolders"/>): what is indexed there stays as it is.</summary>
    Scan,

    /// <summary>Trickplay (<see cref="TrickplayOptions.ExcludedFolders"/>): no new sheets.</summary>
    Trickplay,

    /// <summary>Thumbnails (<see cref="ThumbnailOptions.ExcludedFolders"/>): no new thumbnails.</summary>
    Thumbnails,
}
