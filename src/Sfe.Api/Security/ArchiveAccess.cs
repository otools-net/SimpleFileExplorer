using Sfe.Api.Endpoints;
using Sfe.Core.Access;
using Sfe.Core.Files;
using Sfe.Core.Paths;

namespace Sfe.Api.Security;

/// <summary>
/// What a person may put in a zip (a download, or a compression into the library): what they see and may download, by
/// their access at the time it is made; never the server's files nor a work folder.
/// </summary>
public sealed class ArchiveAccess(AccessState state, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work) : IArchiveAccess
{
    public ArchiveVerdict Check(string path) => state.Check(paths, path, Rights.ViewFiles) switch
    {
        PathVerdict.NotFound => ArchiveVerdict.Hidden,
        PathVerdict.Allowed when state.Check(paths, path, Rights.Download) == PathVerdict.Allowed => ArchiveVerdict.Allowed,
        _ => ArchiveVerdict.Refused,
    };

    public bool IsProtected(string fullPath) => FileEndpoints.Protected(fullPath, serverFiles, work);

    public bool IsProtectedReal(string realPath) => serverFiles.ContainsReal(realPath) || work.Contains(realPath);
}
