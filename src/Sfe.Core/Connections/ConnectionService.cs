using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Logs;
using Sfe.Core.Settings;

namespace Sfe.Core.Connections;

/// <summary>
/// The connections of the server: listed for those who manage them, made from a sign-in's grant, changed, signed in
/// again, removed, and opened. Checks what keeps a connection meaningful (a known kind, a level the settings allow,
/// a name, a root folder) but not who asks: the Api decides that, and <see cref="ConnectionAccess"/> who uses one.
/// Who uses a connection (libraries, groups): <see cref="ConnectionSharing"/>.
/// </summary>
public sealed class ConnectionService(
    IDbContextFactory<AccessDbContext> dbFactory,
    ConnectionProviders providers,
    ICredentialProtector protector,
    SettingLayers settings,
    ILogger<ConnectionService> logger
)
{
    /// <summary>The problem of a connection that cannot be opened before someone signs in to it again.</summary>
    public const string NeedsReconnectingProblem = "The connection must be signed in to again.";

    public const string UnusableProblem = "The connection cannot be used: it is off, or not allowed on this server.";

    public const int MaxNameLength = 200;

    /// <summary>The account-level connections of <paramref name="ownerId"/>.</summary>
    public Task<IReadOnlyList<ConnectionInfo>> GetOwnedAsync(
        int ownerId,
        CancellationToken cancellationToken = default
    ) => ListAsync(c => c.Level == ConnectionLevel.Account && c.OwnerId == ownerId, null, cancellationToken);

    /// <summary>
    /// What a library's administrators manage: its own connections and the server's offered to it, with the groups
    /// of that library only.
    /// </summary>
    public Task<IReadOnlyList<ConnectionInfo>> GetOfLibraryAsync(
        int tenantId,
        CancellationToken cancellationToken = default
    ) =>
        ListAsync(
            c =>
                (c.Level == ConnectionLevel.Library && c.TenantId == tenantId)
                || (c.Level == ConnectionLevel.Server && (c.AllTenants || c.Tenants.Any(t => t.TenantId == tenantId))),
            tenantId,
            cancellationToken
        );

    /// <summary>Every connection of a level (for the server's administrators).</summary>
    public Task<IReadOnlyList<ConnectionInfo>> GetAtLevelAsync(
        ConnectionLevel level,
        CancellationToken cancellationToken = default
    ) => ListAsync(c => c.Level == level, null, cancellationToken);

    public async Task<ConnectionInfo?> FindAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var connection = await db
            .Connections.AsNoTracking()
            .WithUses()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return connection is null ? null : ConnectionRows.ToInfo(connection);
    }

    /// <summary>Makes a connection signed in with <paramref name="grant"/>; nobody but its owner uses it yet.</summary>
    public async Task<AccessResult<ConnectionInfo>> CreateAsync(
        ConnectionDraft draft,
        ConnectionGrant grant,
        int? createdById,
        CancellationToken cancellationToken = default
    )
    {
        var checkedDraft = await CheckAsync(draft, cancellationToken);
        if (checkedDraft.Status != AccessStatus.Ok)
            return checkedDraft.As<ConnectionInfo>();

        var (name, rootPath) = checkedDraft.Value!;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var connection = new Connection
        {
            Kind = providers.Find(draft.Kind)!.Kind,
            Name = name,
            Level = draft.Level,
            OwnerId = draft.Level == ConnectionLevel.Account ? draft.OwnerId : null,
            TenantId = draft.Level == ConnectionLevel.Library ? draft.TenantId : null,
            RootPath = rootPath,
            Created = DateTime.UtcNow,
            CreatedById = createdById,
        };
        SignIn(connection, grant);
        db.Connections.Add(connection);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogSecurityWarning(
            "Connection {Connection} made by account {UserId}",
            ConnectionRows.Describe(connection),
            createdById
        );
        return AccessResult<ConnectionInfo>.Ok(ConnectionRows.ToInfo(connection));
    }

    /// <summary>
    /// Whether <paramref name="draft"/> can be made now (known and allowed kind and level, owner or library, name,
    /// root folder): asked before a sign-in sends the person to the provider, and again when it is made. The value is
    /// the name and root folder as stored.
    /// </summary>
    public async Task<AccessResult<(string Name, string RootPath)>> CheckAsync(
        ConnectionDraft draft,
        CancellationToken cancellationToken = default
    )
    {
        if (DraftError(draft) is { } error)
            return AccessResult<(string, string)>.Invalid(error.Key, error.Message);
        if (NameError(draft.Name, out var name) is { } nameError)
            return AccessResult<(string, string)>.Invalid("name", nameError);
        if (RemotePaths.Normalize(draft.RootPath) is not { } rootPath)
            return AccessResult<(string, string)>.Invalid("rootPath", "Not a folder path.");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await OwnerErrorAsync(db, draft, cancellationToken) is { } ownerError)
            return AccessResult<(string, string)>.Invalid(ownerError.Key, ownerError.Message);
        return AccessResult<(string, string)>.Ok((name, rootPath));
    }

    /// <summary>Renames it, turns it off or on, read only or not, or roots it at another folder.</summary>
    public async Task<AccessResult<ConnectionInfo>> UpdateAsync(
        int id,
        ConnectionChange change,
        int? changedById,
        CancellationToken cancellationToken = default
    )
    {
        var name = string.Empty;
        if (change.Name is not null && NameError(change.Name, out name) is { } nameError)
            return AccessResult<ConnectionInfo>.Invalid("name", nameError);
        var rootPath = change.RootPath is null ? null : RemotePaths.Normalize(change.RootPath);
        if (change.RootPath is not null && rootPath is null)
            return AccessResult<ConnectionInfo>.Invalid("rootPath", "Not a folder path.");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var connection = await db.Connections.WithUses().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (connection is null)
            return AccessResult<ConnectionInfo>.Missing();

        if (change.Name is not null)
            connection.Name = name;
        connection.Disabled = change.Disabled ?? connection.Disabled;
        connection.ReadOnly = change.ReadOnly ?? connection.ReadOnly;
        connection.RootPath = rootPath ?? connection.RootPath;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Connection {Connection} changed by account {UserId}: disabled {Disabled}, read-only {ReadOnly}, at {Root}",
            ConnectionRows.Describe(connection),
            changedById,
            connection.Disabled,
            connection.ReadOnly,
            connection.RootPath
        );
        return AccessResult<ConnectionInfo>.Ok(ConnectionRows.ToInfo(connection));
    }

    /// <summary>
    /// Signed in again (the provider refused the old sign-in, or its keys were lost): the same connection, groups
    /// and rules; the result tells when the provider's account is another one.
    /// </summary>
    public async Task<AccessResult<ConnectionReauthorized>> ReauthorizeAsync(
        int id,
        ConnectionGrant grant,
        int? changedById,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var connection = await db.Connections.WithUses().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (connection is null)
            return AccessResult<ConnectionReauthorized>.Missing();

        var otherAccount = connection.AccountId is { } before && grant.AccountId is { } after && before != after;
        SignIn(connection, grant);
        await db.SaveChangesAsync(cancellationToken);

        if (otherAccount)
        {
            logger.LogSecurityWarning(
                "Connection {Connection} signed in again to another account by account {UserId}",
                ConnectionRows.Describe(connection),
                changedById
            );
        }
        else
        {
            logger.LogInformation(
                "Connection {Connection} signed in again by account {UserId}",
                ConnectionRows.Describe(connection),
                changedById
            );
        }
        return AccessResult<ConnectionReauthorized>.Ok(new(ConnectionRows.ToInfo(connection), otherAccount));
    }

    /// <summary>The provider refused its sign-in: nobody opens it until someone signs in again.</summary>
    public async Task MarkNeedsReconnectingAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var changed = await db
            .Connections.Where(c => c.Id == id && c.State != ConnectionState.NeedsReconnecting)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.State, ConnectionState.NeedsReconnecting), cancellationToken);
        if (changed > 0)
            logger.LogWarning("Connection {ConnectionId} needs to be signed in to again", id);
    }

    /// <summary>Removes it with who uses it and its folder rules; the remote storage is left as it is.</summary>
    public async Task<AccessStatus> RemoveAsync(int id, int? removedById, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var connection = await db
            .Connections.WithUsesAndRules()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (connection is null)
            return AccessStatus.NotFound;

        db.Connections.Remove(connection);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Connection {Connection} removed by account {UserId}",
            ConnectionRows.Describe(connection),
            removedById
        );
        return AccessStatus.Ok;
    }

    /// <summary>
    /// The files of a connection, for whoever <see cref="ConnectionAccess"/> let in. A conflict when it cannot be
    /// opened (off, not allowed, <see cref="NeedsReconnectingProblem"/>).
    /// </summary>
    public async Task<AccessResult<IRemoteStorage>> OpenAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var connection = await db.Connections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (connection is null)
            return AccessResult<IRemoteStorage>.Missing();

        var options = settings.ServerLevel().Connections;
        var provider = providers.Find(connection.Kind);
        if (
            provider is null
            || connection.Disabled
            || !options.Enabled
            || !ConnectionLevels.IsUsable(connection.Kind, connection.Level, options)
        )
        {
            return AccessResult<IRemoteStorage>.Conflict(UnusableProblem);
        }
        if (connection.State == ConnectionState.NeedsReconnecting || connection.Credentials is null)
            return AccessResult<IRemoteStorage>.Conflict(NeedsReconnectingProblem);

        if (protector.Unprotect(connection.Credentials) is not { } credentials)
        {
            // Protected with other keys (a backup restored without its keys folder).
            await MarkNeedsReconnectingAsync(id, cancellationToken);
            return AccessResult<IRemoteStorage>.Conflict(NeedsReconnectingProblem);
        }
        return AccessResult<IRemoteStorage>.Ok(provider.Open(credentials, connection.RootPath, options));
    }

    private void SignIn(Connection connection, ConnectionGrant grant)
    {
        connection.Credentials = protector.Protect(grant.Credentials);
        connection.Account = Clip(grant.Account);
        connection.AccountId = Clip(grant.AccountId);
        connection.State = ConnectionState.Ready;
    }

    private async Task<IReadOnlyList<ConnectionInfo>> ListAsync(
        System.Linq.Expressions.Expression<Func<Connection, bool>> filter,
        int? tenantId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var connections = await db
            .Connections.AsNoTracking()
            .Where(filter)
            .WithUses()
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return
        [
            .. connections
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(c => c.Id)
                .Select(c => ConnectionRows.ToInfo(c, tenantId)),
        ];
    }

    /// <summary>The kind is known and allowed, at a level the settings allow, with its owner or library.</summary>
    private (string Key, string Message)? DraftError(ConnectionDraft draft)
    {
        var options = settings.ServerLevel().Connections;
        if (providers.Find(draft.Kind) is null)
            return ("kind", "This server does not know this kind of connection.");
        if (!options.Enabled || !ConnectionProviders.IsAllowed(draft.Kind, options))
            return ("kind", "Connections of this kind are not allowed on this server.");
        if (!ConnectionLevels.IsAllowed(draft.Level, options))
            return ("level", "Connections of this level are not allowed on this server.");
        return draft.Level switch
        {
            ConnectionLevel.Account when draft.OwnerId is null => (
                "ownerId",
                "An account-level connection has an owner."
            ),
            ConnectionLevel.Library when draft.TenantId is null => (
                "tenantId",
                "A library-level connection has a library."
            ),
            _ => null,
        };
    }

    private static async Task<(string Key, string Message)?> OwnerErrorAsync(
        AccessDbContext db,
        ConnectionDraft draft,
        CancellationToken cancellationToken
    )
    {
        if (
            draft.Level == ConnectionLevel.Account
            && !await db.Users.AnyAsync(u => u.Id == draft.OwnerId, cancellationToken)
        )
            return ("ownerId", "No such account.");
        if (
            draft.Level == ConnectionLevel.Library
            && !await db.Tenants.AnyAsync(t => t.Id == draft.TenantId, cancellationToken)
        )
            return ("tenantId", "No such library.");
        return null;
    }

    private static string? NameError(string? name, out string trimmed)
    {
        trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return "A connection has a name.";
        if (trimmed.Length > MaxNameLength || trimmed.Any(char.IsControl))
            return $"A name of at most {MaxNameLength} characters, without control characters.";
        return null;
    }

    private static string? Clip(string? text) => text is { Length: > 400 } ? text[..400] : text;
}
