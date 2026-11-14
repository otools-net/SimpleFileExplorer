using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Core.Tasks;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the routes of background tasks take from the container (bound with <c>[AsParameters]</c>; none of it is in
/// the OpenAPI document).
/// </summary>
internal sealed record TaskServices(
    BackgroundTasks Tasks,
    TaskScheduleService Schedules,
    LibraryPaths Paths,
    CurrentAccess Current
);
