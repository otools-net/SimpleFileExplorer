namespace Sfe.Core.Data;

/// <summary>
/// A personal access token: a program presenting it acts as <see cref="UserId"/>, with at most the account's rights
/// (<see cref="Access.AccessTokens"/>). Only a hash of its secret is kept; the token is shown once, when made. Removed
/// with its account and with the library it is limited to.
/// </summary>
public sealed class AccessToken
{
    public const int MaxNameLength = 100;
    public const int LookupLength = 16;
    public const int MaxPrefixLength = 32;
    public const int HashLength = 32;
    public const int MaxClientIdLength = 200;
    public const int MaxAddressLength = 64;

    public int Id { get; set; }

    /// <summary>The account the token acts as.</summary>
    public int UserId { get; set; }

    /// <summary>What its owner called it, to tell it from the others.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The public part of the token that finds its row (random, unique); never enough to use it.</summary>
    public string Lookup { get; set; } = string.Empty;

    /// <summary>SHA-256 of the token's secret bytes.</summary>
    public byte[] Hash { get; set; } = [];

    /// <summary>The token's first characters, shown in lists so that its owner recognises it.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>The only library it may be used in; null: those its account reaches.</summary>
    public int? TenantId { get; set; }

    /// <summary>
    /// The rights it is limited to, separated by spaces; null: all of its account's. Always intersected with the
    /// account's rights at the time of use.
    /// </summary>
    public string? Rights { get; set; }

    public AccessTokenKind Kind { get; set; }

    /// <summary>The program it was given to (OAuth, later); null for a personal token.</summary>
    public string? ClientId { get; set; }

    public DateTime Created { get; set; }

    /// <summary>When it stops working; null: never.</summary>
    public DateTime? Expires { get; set; }

    /// <summary>Last time it was used, written at most every few minutes.</summary>
    public DateTime? LastUsed { get; set; }

    /// <summary>The network address it was last used from.</summary>
    public string? LastUsedFrom { get; set; }

    /// <summary>When it was revoked; a revoked token never works again.</summary>
    public DateTime? Revoked { get; set; }

    /// <summary>The account that revoked it; no foreign key (a second cascade path from the accounts).</summary>
    public int? RevokedById { get; set; }
}
