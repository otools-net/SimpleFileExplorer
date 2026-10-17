using Sfe.Api.Endpoints;
using Sfe.Core.Access;
using Sfe.Core.Files;
using Sfe.Core.Paths;

namespace Sfe.Api.Security;

/// <summary>
/// What the person who started a move or copy may do, read again at each item of the job (agents/docs/features/
/// move-and-copy.md): rights, folder rules and the account itself (disabled meanwhile: nothing more) can change while it
/// runs, long after the request that started it.
/// </summary>
public sealed class FileJobAccess(
    AccessService accounts, RightsEvaluator rights, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, TenantInfo tenant, int? userId) : IFileJobAccess
{
    public async Task<bool> MayAsync(string path, string right) =>
        await StateAsync() is { } state && state.Check(paths, path, right) == PathVerdict.Allowed;

    public async Task<bool> MayThroughoutAsync(string path, string right) =>
        await StateAsync() is { } state && state.Check(paths, path, right) == PathVerdict.Allowed
        && (!paths.TryResolve(path, out var full) || paths.ToRelative(full) is not { } canonical
            || state.Access.FolderPolicy.AllowsThroughout(canonical, right, state.Access.Rights));

    public bool IsProtected(string fullPath) => FileEndpoints.Protected(fullPath, serverFiles, work);

    /// <returns>null once the account is gone or disabled.</returns>
    private async Task<AccessState?> StateAsync()
    {
        SessionUser? user = null;
        if (userId is { } id && (user = await accounts.GetSessionUserAsync(id, CancellationToken.None)) is null or { Disabled: true })
            return null;
        return new AccessState(tenant, user, await rights.GetAsync(new AccessSubject(tenant.Id, userId), CancellationToken.None));
    }
}
