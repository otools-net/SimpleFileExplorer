using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Files;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

/// <summary>
/// What the routes of moves, copies and compressions take from the container and the request (bound with
/// <c>[AsParameters]</c>; none of it is in the OpenAPI document).
/// </summary>
internal sealed record FileJobServices(
    FileJobs Jobs,
    AccessService Accounts,
    RightsEvaluator Rights,
    LibraryPaths Paths,
    ServerFiles ServerFiles,
    WorkFolder Work,
    CurrentAccess Current
);
