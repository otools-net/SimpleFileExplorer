using Sfe.Api;

namespace Sfe.Tests.Infrastructure;

/// <summary>
/// The base of the routes the tests call, imported in every test file (<c>Sfe.Tests.csproj</c>):
/// <c>$"{V1}/tags"</c> is <c>/api/v1/tags</c>. The unversioned paths are tested by <c>LegacyPathTests</c> only.
/// </summary>
public static class ApiPaths
{
    /// <summary>The base of every v1 route (<see cref="ApiRoutes.Base"/>).</summary>
    public const string V1 = ApiRoutes.Base;
}
