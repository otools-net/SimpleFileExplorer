using System.Text.Json.Serialization;

namespace Otools.Net.Sfe.Core.Library;

/// <summary>
/// For each kind, the listed folder (root-relative) a folder is excluded through: itself or the nearest of its
/// ancestors on the list; null when not excluded.
/// </summary>
public sealed record FolderExclusionState(string? Scan, string? Trickplay, string? Thumbnails)
{
    public static readonly FolderExclusionState None = new(null, null, null);

    [JsonIgnore]
    public bool IsNone => Scan is null && Trickplay is null && Thumbnails is null;

    public string? Of(ExclusionKind kind) =>
        kind switch
        {
            ExclusionKind.Scan => Scan,
            ExclusionKind.Trickplay => Trickplay,
            _ => Thumbnails,
        };
}
