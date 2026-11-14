using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Core.Trickplay;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the route of trickplay sheets takes from the container (bound with <c>[AsParameters]</c>; none of it is in
/// the OpenAPI document).
/// </summary>
internal sealed record TrickplayServices(TrickplayService Service, LibraryPaths Paths, CurrentAccess Current);
