using Microsoft.Extensions.Options;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Api.Security;

/// <summary>
/// Whether a place reached through a link (symbolic link, junction) that leaves its library may be written to
/// (<see cref="SecurityOptions.LinksOutsideLibrary"/>). Whoever can put a link in a library folder outside SFE (a
/// share, SFTP) would otherwise give SFE's users whatever the server's account may write.
/// </summary>
public sealed class LibraryLinks(IOptionsMonitor<SecurityOptions> security)
{
    /// <summary>
    /// A write at <paramref name="fullPath"/> is refused: links leaving the library are read only, and the path
    /// really is (<see cref="LibraryPaths.RealPath"/>) outside the library and the folders links may lead to
    /// (<see cref="SecurityOptions.LinkTargets"/>). A part that does not exist yet (a new name) is kept as written.
    /// </summary>
    public bool RefusesWrite(LibraryPaths paths, string fullPath)
    {
        var options = security.CurrentValue;
        if (options.LinksOutsideLibrary == LinksOutsideLibrary.Follow || paths.RealRelative(fullPath) is not null)
            return false;

        var real = LibraryPaths.RealPath(fullPath);
        return !options
            .LinkTargets.Where(target => !string.IsNullOrWhiteSpace(target))
            .Select(target => LibraryPaths.RealPath(LibraryPaths.NormalizeRoot(target)))
            .Any(target => LibraryPaths.IsSameOrInside(real, target));
    }
}
