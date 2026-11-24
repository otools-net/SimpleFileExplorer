using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>The previous versions of a file, and what the caller may do with them.</summary>
/// <param name="Items">Newest first (at most <see cref="PreviousVersions.MaxListed"/>); <c>deletedAt</c> is when the
/// file was saved over, <c>deletedBy</c> who saved it.</param>
/// <param name="RestoreOver">Restore a version over the file: the rights of saving over it (as move/copy's Replace),
/// in a space that keeps deleted files in the trash.</param>
/// <param name="RestoreCopy">Restore a version as a copy next to it (<c>files.createFiles</c> in its folder).</param>
/// <param name="Download">Download a version (<c>files.download</c> at the file).</param>
public sealed record FileVersions(
    IReadOnlyList<TrashItem> Items,
    bool RestoreOver,
    bool RestoreCopy,
    bool Download
);

/// <summary>A version restored over its file.</summary>
/// <param name="Item">The file as it is now.</param>
/// <param name="TrashId">The version made of the content it had (in the trash).</param>
public sealed record RestoredVersion(LibraryItem Item, int? TrashId);
