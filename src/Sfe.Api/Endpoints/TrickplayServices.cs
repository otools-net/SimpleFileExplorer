using Sfe.Api.Security;
using Sfe.Core.Paths;
using Sfe.Core.Trickplay;

namespace Sfe.Api.Endpoints;

/// <summary>
/// What the route of trickplay sheets takes from the container (bound with <c>[AsParameters]</c>; none of it is in
/// the OpenAPI document).
/// </summary>
internal sealed record TrickplayServices(TrickplayService Service, LibraryPaths Paths, CurrentAccess Current);
