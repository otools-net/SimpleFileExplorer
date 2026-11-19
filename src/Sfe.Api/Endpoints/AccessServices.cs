using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the routes of accounts and groups take from the container (bound with <c>[AsParameters]</c>; none of it is
/// in the OpenAPI document).
/// </summary>
internal sealed record AccessServices(AccessService Accounts, CurrentAccess Current);
