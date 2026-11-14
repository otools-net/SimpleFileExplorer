using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the routes of the session take from the container and the request (bound with <c>[AsParameters]</c>; none of it
/// is in the OpenAPI document). Server services only: these routes run before a library is chosen.
/// </summary>
internal sealed record SessionServices(
    HttpContext Context,
    AccessService Accounts,
    CurrentAccess Current,
    SignInThrottle Throttle,
    ILoggerFactory Loggers
);
