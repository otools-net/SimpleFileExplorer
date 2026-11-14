using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sfe.Core.Data;
using Sfe.Core.Options;
using Sfe.Core.Settings;

namespace Sfe.Core.Access;

/// <summary>
/// Personal access tokens: an account makes them for programs, which then act as the account with at most its rights
/// (or fewer, as the token says). The whole token is shown once, at creation; only a hash of its secret is kept
/// (<see cref="AccessTokenSecret"/>). Checks what keeps them meaningful (limits of
/// <see cref="AccessTokenOptions"/>, rights among the account's) but not who asks: the Api decides that.
/// </summary>
public sealed class AccessTokens(
    IDbContextFactory<AccessDbContext> dbFactory,
    AccessChanges changes,
    RightsEvaluator rightsEvaluator,
    SettingLayers settings,
    TimeProvider clock,
    ILogger<AccessTokens> logger
)
{
    public const string DisabledProblem = "Access tokens are turned off on this server.";
    public const string TooManyProblem = "The account holds as many tokens as allowed: revoke one first.";

    /// <summary>The last use of a token is written at most this often.</summary>
    public static readonly TimeSpan LastUseInterval = TimeSpan.FromMinutes(5);

    /// <summary>Compared when the lookup finds nothing: an unknown token takes as long as a wrong one.</summary>
    private static readonly byte[] NoHash = new byte[AccessToken.HashLength];

    /// <summary>The rows found by their lookup, while <see cref="AccessChanges.Version"/> is the one read at.</summary>
    private readonly ConcurrentDictionary<string, (long Version, AccessToken Row)> _rows = new(StringComparer.Ordinal);

    /// <summary>When the last use of each token was written.</summary>
    private readonly ConcurrentDictionary<int, DateTime> _usesWritten = new();

    private readonly SemaphoreSlim _creating = new(1, 1);

    /// <summary>The settings in effect: the configuration's, then the server settings page's.</summary>
    public AccessTokenOptions Options => settings.ServerLevel().Security.AccessTokens;

    /// <summary>
    /// Makes a token for account <paramref name="userId"/>: invalid for a missing name, an expiry outside the
    /// allowed life, a library the account doesn't reach or a right it doesn't hold there; conflict when tokens are
    /// off or the account holds as many as allowed.
    /// </summary>
    public async Task<AccessResult<CreatedAccessToken>> CreateAsync(
        int userId,
        AccessTokenDraft draft,
        CancellationToken cancellationToken = default
    )
    {
        var options = Options;
        if (!options.Enabled)
            return AccessResult<CreatedAccessToken>.Conflict(DisabledProblem);

        var now = clock.GetUtcNow().UtcDateTime;
        var invalid = Validate(draft, options, now) ?? await ValidateRightsAsync(userId, draft, cancellationToken);
        if (invalid is not null)
            return invalid;

        await _creating.WaitAsync(cancellationToken);
        try
        {
            return await AddAsync(userId, draft, options, now, cancellationToken);
        }
        finally
        {
            _creating.Release();
        }
    }

    /// <summary>The tokens of account <paramref name="userId"/>, revoked and expired ones too, newest first.</summary>
    public Task<IReadOnlyList<AccessTokenInfo>> ListAsync(int userId, CancellationToken cancellationToken = default) =>
        QueryAsync((_, tokens) => tokens.Where(t => t.UserId == userId), cancellationToken);

    /// <summary>
    /// Every account's tokens (<paramref name="tenantIds"/> null), or those of the accounts belonging to one of
    /// <paramref name="tenantIds"/>; newest first.
    /// </summary>
    public Task<IReadOnlyList<AccessTokenInfo>> ListAllAsync(
        IReadOnlyCollection<int>? tenantIds,
        CancellationToken cancellationToken = default
    ) =>
        QueryAsync(
            (db, tokens) =>
                tenantIds is null
                    ? tokens
                    : tokens.Where(t =>
                        db.GroupMembers.Any(m => m.UserId == t.UserId && tenantIds.Contains(m.Group!.TenantId))
                    ),
            cancellationToken
        );

    /// <summary>
    /// Revokes token <paramref name="tokenId"/>; with <paramref name="ownerId"/>, only if it is that account's (not
    /// found otherwise). Revoking a revoked token changes nothing.
    /// </summary>
    public async Task<AccessStatus> RevokeAsync(
        int tokenId,
        int? ownerId,
        int revokedById,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var token = db.AccessTokens.Where(t => t.Id == tokenId && (ownerId == null || t.UserId == ownerId));
        if (!await token.AnyAsync(cancellationToken))
            return AccessStatus.NotFound;

        var revoked = await RevokeRowsAsync(token, revokedById, cancellationToken);
        if (revoked > 0)
            logger.LogInformation("Access token {TokenId} revoked by account {RevokedBy}", tokenId, revokedById);
        return AccessStatus.Ok;
    }

    /// <summary>Revokes every token of account <paramref name="userId"/>; returns how many were in use.</summary>
    public async Task<int> RevokeAllAsync(int userId, int revokedById, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var revoked = await RevokeRowsAsync(
            db.AccessTokens.Where(t => t.UserId == userId),
            revokedById,
            cancellationToken
        );
        if (revoked > 0)
        {
            logger.LogInformation(
                "{Count} access tokens of account {UserId} revoked by account {RevokedBy}",
                revoked,
                userId,
                revokedById
            );
        }
        return revoked;
    }

    /// <summary>
    /// The token a program presented, when it is one, its secret matches (compared in constant time), and it is
    /// neither revoked nor expired, and tokens are on; null otherwise. Notes its use (written at most every
    /// <see cref="LastUseInterval"/>) with the address it came from.
    /// </summary>
    public async Task<PresentedAccessToken?> FindAsync(
        string? presented,
        string? address,
        CancellationToken cancellationToken = default
    )
    {
        if (!AccessTokenSecret.TryParse(presented, out var lookup, out var hash) || !Options.Enabled)
            return null;

        var row = await RowAsync(lookup, cancellationToken);
        var matches = AccessTokenSecret.Matches(hash, row?.Hash ?? NoHash);
        var now = clock.GetUtcNow().UtcDateTime;
        if (row is null || !matches || row.Revoked is not null || row.Expires <= now)
            return null;

        await NoteUseAsync(row.Id, address, now, cancellationToken);
        return new(row.Id, row.UserId, row.Name, row.TenantId, RightsOf(row.Rights)?.ToHashSet(), row.Expires);
    }

    private static AccessResult<CreatedAccessToken>? Validate(
        AccessTokenDraft draft,
        AccessTokenOptions options,
        DateTime now
    )
    {
        var name = draft.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > AccessToken.MaxNameLength)
        {
            return AccessResult<CreatedAccessToken>.Invalid(
                "name",
                $"Name the token (at most {AccessToken.MaxNameLength} characters)."
            );
        }
        var latest = options.MaxLifetimeDays > 0 ? now.AddDays(options.MaxLifetimeDays) : DateTime.MaxValue;
        if (draft.Expires is { } expires && (expires <= now || expires > latest))
        {
            var within = options.MaxLifetimeDays > 0 ? $", within {options.MaxLifetimeDays} days" : string.Empty;
            return AccessResult<CreatedAccessToken>.Invalid("expires", $"A token expires in the future{within}.");
        }
        return null;
    }

    /// <summary>The library must be one the account reaches, the rights known and held by the account there.</summary>
    private async Task<AccessResult<CreatedAccessToken>?> ValidateRightsAsync(
        int userId,
        AccessTokenDraft draft,
        CancellationToken cancellationToken
    )
    {
        var reached = await rightsEvaluator.GetTenantIdsAsync(userId, cancellationToken);
        if (draft.TenantId is { } tenantId && !reached.Contains(tenantId))
            return AccessResult<CreatedAccessToken>.Invalid("tenantId", "Choose a space the account reaches.");

        var wanted = draft.Rights ?? [];
        if (wanted.Count == 0)
            return null;

        var held = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tenant in draft.TenantId is { } only ? [only] : reached)
            held.UnionWith((await rightsEvaluator.GetAsync(new(tenant, userId), cancellationToken)).Rights);
        var foreign = wanted.FirstOrDefault(right => RightCatalog.Find(right) is null || !held.Contains(right));
        if (foreign is null)
            return null;
        var message = $"The account doesn't hold the right “{foreign}”.";
        return AccessResult<CreatedAccessToken>.Invalid("rights", message);
    }

    private async Task<AccessResult<CreatedAccessToken>> AddAsync(
        int userId,
        AccessTokenDraft draft,
        AccessTokenOptions options,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var held = await db.AccessTokens.CountAsync(
            t => t.UserId == userId && t.Revoked == null && (t.Expires == null || t.Expires > now),
            cancellationToken
        );
        if (held >= options.MaxPerAccount)
            return AccessResult<CreatedAccessToken>.Conflict(TooManyProblem);

        var (lookup, hash, token) = AccessTokenSecret.New();
        var row = new AccessToken
        {
            UserId = userId,
            Name = draft.Name!.Trim(),
            Lookup = lookup,
            Hash = hash,
            Prefix = AccessTokenSecret.Prefix(token),
            TenantId = draft.TenantId,
            Rights = draft.Rights is { Count: > 0 } rights ? string.Join(' ', rights.Distinct().Order()) : null,
            Kind = AccessTokenKind.Personal,
            Created = now,
            Expires = draft.Expires ?? (options.MaxLifetimeDays > 0 ? now.AddDays(options.MaxLifetimeDays) : null),
        };
        db.AccessTokens.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Access token {TokenId} “{Name}” made by account {UserId}", row.Id, row.Name, userId);

        var userName = await db.Users.Where(u => u.Id == userId).Select(u => u.UserName).SingleAsync(cancellationToken);
        return AccessResult<CreatedAccessToken>.Ok(new(ToInfo(row, userName), token));
    }

    private async Task<int> RevokeRowsAsync(
        IQueryable<AccessToken> tokens,
        int revokedById,
        CancellationToken cancellationToken
    )
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var revoked = await tokens
            .Where(t => t.Revoked == null)
            .ExecuteUpdateAsync(
                set => set.SetProperty(t => t.Revoked, now).SetProperty(t => t.RevokedById, revokedById),
                cancellationToken
            );
        // The cached rows were read before: they are read again.
        changes.Changed();
        return revoked;
    }

    private async Task<AccessToken?> RowAsync(string lookup, CancellationToken cancellationToken)
    {
        var version = changes.Version;
        if (_rows.TryGetValue(lookup, out var cached) && cached.Version == version)
            return cached.Row;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.AccessTokens.AsNoTracking().SingleOrDefaultAsync(t => t.Lookup == lookup, cancellationToken);
        // Unknown lookups are not kept: guesses would fill the cache.
        if (row is not null)
            _rows[lookup] = (version, row);
        return row;
    }

    /// <summary>Writes the token's last use when the one written before is older than the interval.</summary>
    private async Task NoteUseAsync(int tokenId, string? address, DateTime now, CancellationToken cancellationToken)
    {
        var isDue = _usesWritten.TryGetValue(tokenId, out var written)
            ? now - written >= LastUseInterval && _usesWritten.TryUpdate(tokenId, now, written)
            : _usesWritten.TryAdd(tokenId, now);
        if (!isDue)
            return;

        try
        {
            await WriteUseAsync(tokenId, Truncated(address), now, cancellationToken);
        }
        catch (DbException ex)
        {
            // The use is a note: a busy database doesn't refuse the request.
            logger.LogWarning(ex, "The last use of access token {TokenId} could not be written", tokenId);
        }
    }

    private async Task WriteUseAsync(int tokenId, string? address, DateTime now, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db
            .AccessTokens.Where(t => t.Id == tokenId)
            .ExecuteUpdateAsync(
                set => set.SetProperty(t => t.LastUsed, now).SetProperty(t => t.LastUsedFrom, address),
                cancellationToken
            );
    }

    private async Task<IReadOnlyList<AccessTokenInfo>> QueryAsync(
        Func<AccessDbContext, IQueryable<AccessToken>, IQueryable<AccessToken>> filter,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await filter(db, db.AccessTokens.AsNoTracking())
            .Join(db.Users, t => t.UserId, u => u.Id, (t, u) => new { Token = t, u.UserName })
            .OrderByDescending(r => r.Token.Created)
            .ThenByDescending(r => r.Token.Id)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => ToInfo(r.Token, r.UserName))];
    }

    private static AccessTokenInfo ToInfo(AccessToken row, string userName) =>
        new(
            row.Id,
            row.UserId,
            userName,
            row.Name,
            row.Prefix,
            row.TenantId,
            RightsOf(row.Rights) ?? [],
            row.Kind,
            row.Created,
            row.Expires,
            row.LastUsed,
            row.LastUsedFrom,
            row.Revoked,
            row.RevokedById
        );

    private static string[]? RightsOf(string? rights) =>
        string.IsNullOrEmpty(rights) ? null : rights.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string? Truncated(string? address) =>
        address is { Length: > AccessToken.MaxAddressLength } ? address[..AccessToken.MaxAddressLength] : address;
}
