using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

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
)
{
    /// <summary>
    /// What the person who starts a job may do, read again at each item (with the request's access token's limits).
    /// </summary>
    public FileJobAccess AccessFor(TenantInfo tenant, AccessState state) =>
        new(Accounts, Rights, Paths, ServerFiles, Work, tenant, state.User?.Id, Current.JobToken());
}
