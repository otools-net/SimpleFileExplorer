using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Editing;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the routes of the picture editor take from the container and the request (bound with <c>[AsParameters]</c>;
/// none of it is in the OpenAPI document).
/// </summary>
internal sealed record EditorServices(
    EditedImages Editor,
    FileContent Content,
    Trash Trash,
    LibraryPaths Paths,
    ServerFiles ServerFiles,
    WorkFolder Work,
    CurrentAccess Current,
    HttpContext Context
);
