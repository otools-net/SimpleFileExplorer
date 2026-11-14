using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Logs;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the routes of the log take from the container (bound with <c>[AsParameters]</c>; none of it is in the OpenAPI
/// document).
/// </summary>
internal sealed record LogServices(AuditLogService Log, CurrentAccess Current);
