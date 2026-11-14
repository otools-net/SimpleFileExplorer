using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Access;

/// <summary>A new personal access token, as its account asks for it.</summary>
/// <param name="Expires">When it stops working (UTC); null: the longest life allowed, or never when there is no
/// limit (<see cref="Options.AccessTokenOptions.MaxLifetimeDays"/>).</param>
/// <param name="TenantId">The only library it may be used in; null: those the account reaches.</param>
/// <param name="Rights">Rights it is limited to, among the account's own; null or empty: all of the account's.</param>
public sealed record AccessTokenDraft(
    string? Name,
    DateTime? Expires = null,
    int? TenantId = null,
    IReadOnlyList<string>? Rights = null
);

/// <summary>A token as lists show it: never its secret.</summary>
/// <param name="Prefix">Its first characters, to recognise it.</param>
/// <param name="Rights">The rights it is limited to; empty: all of its account's.</param>
public sealed record AccessTokenInfo(
    int Id,
    int UserId,
    string UserName,
    string Name,
    string Prefix,
    int? TenantId,
    IReadOnlyList<string> Rights,
    AccessTokenKind Kind,
    DateTime Created,
    DateTime? Expires,
    DateTime? LastUsed,
    string? LastUsedFrom,
    DateTime? Revoked,
    int? RevokedById
);

/// <summary>A token just made, with the whole token: shown once, never stored nor given again.</summary>
public sealed record CreatedAccessToken(AccessTokenInfo Token, string Secret);

/// <summary>
/// A token presented by a program, found valid (known, matching, not revoked nor expired): who it acts as and what
/// limits it. The account's own state and rights are checked by the caller.
/// </summary>
/// <param name="Rights">The rights it is limited to; null: all of its account's.</param>
public sealed record PresentedAccessToken(
    int Id,
    int UserId,
    string Name,
    int? TenantId,
    IReadOnlySet<string>? Rights,
    DateTime? Expires
);
