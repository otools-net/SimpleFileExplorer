using Sfe.Api.Endpoints;
using Sfe.Core.Access;
using Sfe.Core.Files;
using Sfe.Core.Paths;

namespace Sfe.Api.Security;

/// <summary>
/// What the person who started a move or copy may do, read again at each item of the job (agents/docs/features/
/// move-and-copy.md): rights, folder rules and the account itself (disabled meanwhile: nothing more) can change while
/// it runs, long after the request that started it. A job started with an access token (<c>token</c>) keeps the
/// token's limits, and stops once the token is revoked or expired.
/// </summary>
public sealed class FileJobAccess(
    AccessService accounts,
    RightsEvaluator rights,
    LibraryPaths paths,
    ServerFiles serverFiles,
    WorkFolder work,
    TenantInfo tenant,
    int? userId,
    JobToken? token = null
) : IFileJobAccess
{
    public async Task<bool> MayAsync(string path, string right) =>
        await StateAsync() is { } state && state.Check(paths, path, right) == PathVerdict.Allowed;

    public async Task<bool> MayThroughoutAsync(string path, string right) =>
        await StateAsync() is { } state
        && state.Check(paths, path, right) == PathVerdict.Allowed
        && (
            !paths.TryResolve(path, out var full)
            || paths.ToRelative(full) is not { } canonical
            || state.Access.FolderPolicy.AllowsThroughout(canonical, right, state.Access.Rights)
        );

    public async Task<string?> StoppedAsync() =>
        token is not null && !await token.IsActiveAsync() ? JobToken.StoppedProblem : null;

    public bool IsProtected(string fullPath) => FileEndpoints.Protected(fullPath, serverFiles, work);

    public bool IsReadOnly(string fullPath) => FileEndpoints.ProtectedFromWrites(fullPath, serverFiles, work);

    public async Task<IArchiveAccess?> ArchiveAccessAsync() =>
        await StateAsync() is { } state ? new ArchiveAccess(state, paths, serverFiles, work) : null;

    /// <returns>null once the account is gone or disabled, or the job's token no longer works.</returns>
    private async Task<AccessState?> StateAsync()
    {
        SessionUser? user = null;
        if (
            userId is { } id
            && (user = await accounts.GetSessionUserAsync(id, CancellationToken.None)) is null or { Disabled: true }
        )
        {
            return null;
        }

        if (token is not null && !await token.IsActiveAsync())
            return null;

        var access = await rights.GetAsync(new AccessSubject(tenant.Id, userId), CancellationToken.None);
        return new AccessState(tenant, user, token?.Limit(access) ?? access);
    }
}
