using System.Text.Json.Serialization;

namespace Sfe.Core.Options;

/// <summary>Where file management keeps its work files (the trash, uploads not finished yet).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkFolderLocation>))]
public enum WorkFolderLocation
{
    /// <summary>
    /// A hidden folder at the library's root (<see cref="Sfe.Core.Files.WorkFolder.Name"/>): on the library's own
    /// drive, so that moving a file there is a rename, however large the file.
    /// </summary>
    Library,

    /// <summary>
    /// In the server's data folder, beside the thumbnails: the library holds nothing of SFE's, but a file going to the
    /// trash or an upload finishing is copied from one drive to the other.
    /// </summary>
    Server,
}
