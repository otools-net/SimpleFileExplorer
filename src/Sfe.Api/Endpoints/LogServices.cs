using Sfe.Api.Security;
using Sfe.Core.Logs;

namespace Sfe.Api.Endpoints;

/// <summary>
/// What the routes of the log take from the container (bound with <c>[AsParameters]</c>; none of it is in the OpenAPI
/// document).
/// </summary>
internal sealed record LogServices(AuditLogService Log, CurrentAccess Current);
