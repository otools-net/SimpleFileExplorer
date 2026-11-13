using Sfe.Api.Security;
using Sfe.Core.Editing;
using Sfe.Core.Files;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

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
