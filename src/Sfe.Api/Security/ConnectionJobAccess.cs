using Sfe.Api.Endpoints;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Transfers;

namespace Sfe.Api.Security;

/// <summary>
/// What the person who started a transfer may do in one of its connections, read again as the job goes (as
/// <see cref="FileJobAccess"/> for the library): the account disabled, the connection no longer theirs to use, a
/// right or a folder rule changed apply to the next item. Read again only when accounts, groups, rights,
/// connections or rules changed (<see cref="AccessChanges"/>).
/// </summary>
public sealed class ConnectionJobAccess(ConnectionJobServices services, ConnectionJobSubject who) : IConnectionJobAccess
{
    private Reading? _last;

    /// <summary>The access read at a version of <see cref="AccessChanges"/>; null folders: none any more.</summary>
    private sealed record Reading(long Version, ConnectionFolders? Folders);

    public async Task<ConnectionFolders?> FoldersAsync(CancellationToken cancellationToken)
    {
        var version = services.Changes.Version;
        if (_last is { } last && last.Version == version)
            return last.Folders;
        var folders = await ReadAsync(cancellationToken);
        _last = new Reading(version, folders);
        return folders;
    }

    private async Task<ConnectionFolders?> ReadAsync(CancellationToken cancellationToken)
    {
        if (who.UserId is not { } userId)
            return null;
        var user = await services.Accounts.GetSessionUserAsync(userId, cancellationToken);
        if (user is null || user.Disabled)
            return null;

        if (who.Token is { } token && !await token.IsActiveAsync(cancellationToken))
            return null;

        var access = await services.Rights.GetAsync(new AccessSubject(who.Tenant.Id, userId), cancellationToken);
        access = who.Token?.Limit(access) ?? access;
        if (!access.Has(Rights.UseConnections))
            return null;
        var subject = ConnectionEndpoints.SubjectOf(new AccessState(who.Tenant, user, access));
        if (await services.Connections.FindAsync(subject, who.ConnectionId, cancellationToken) is null)
            return null;
        return await services.Connections.FoldersAsync(
            subject,
            who.ConnectionId,
            (access.Rights, who.Comparison),
            cancellationToken
        );
    }
}
