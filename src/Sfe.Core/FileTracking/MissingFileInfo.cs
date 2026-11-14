using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Tagging;

namespace Otools.Net.Sfe.Core.FileTracking;

/// <param name="Path">Where the file was, relative to the library (absolute when outside it).</param>
/// <param name="Candidates">Indexed files that may be it, same name first.</param>
public sealed record MissingFileInfo(
    int Id,
    string Path,
    string Name,
    long Size,
    DateTimeOffset Modified,
    DateTimeOffset MissingSince,
    IReadOnlyList<TagSummary> Tags,
    IReadOnlyList<LibraryItem> Candidates
);
