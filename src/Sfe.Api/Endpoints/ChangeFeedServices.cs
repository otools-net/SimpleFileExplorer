using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Connections.Indexing;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.KeepAlive;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Library.Watching;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Core.Tasks;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the change feed takes from the container for the library's folders, keep alive, the trash and the runs of
/// background tasks that ended (bound with <c>[AsParameters]</c>; none of it is in the OpenAPI document).
/// </summary>
internal sealed record ChangeFeedServices(
    LibraryBrowser Browser,
    FolderWatchers Watchers,
    ConnectionWatchers ConnectionWatchers,
    KeepAliveService KeepAlive,
    Trash Trash,
    ItemsBeingWritten Writing,
    TaskEndings TaskEndings,
    LibraryPaths Paths,
    CurrentAccess Current,
    IHttpContextAccessor Accessor
);
