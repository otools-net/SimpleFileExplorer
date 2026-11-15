using Otools.Net.Sfe.Core.Tagging;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The query of <c>GET api/connections/{id}/file-tags/scope</c> (bound with <c>[AsParameters]</c>): the folder
/// <c>path</c> (with its sub-folders when <c>recursive</c>), or what the search <c>q</c> finds in it.
/// </summary>
internal sealed record ConnectionTagScopeQuery(string? Path = null, bool Recursive = false, string? Q = null)
{
    public TagScope Scope() => new(Path, Recursive, Q);
}
