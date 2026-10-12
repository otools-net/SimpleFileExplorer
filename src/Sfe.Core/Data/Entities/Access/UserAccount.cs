namespace Sfe.Core.Data;

/// <summary>
/// An account, for the whole server: the libraries it reaches follow from its groups (<see cref="GroupMember"/>).
/// Local accounts have a password here; accounts of a directory (<see cref="Source"/> other than <c>local</c>, e.g.
/// LDAP) are checked there and recorded here on their first sign-in, with no password.
/// </summary>
public sealed class UserAccount
{
    public const string LocalSource = "local";

    public int Id { get; set; }

    /// <summary>The name signed in with, unique whatever the case.</summary>
    public string UserName { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    /// <summary>Where the password is checked: <see cref="LocalSource"/> or a directory's name.</summary>
    public string Source { get; set; } = LocalSource;

    /// <summary>The directory's own id of the account (e.g. an LDAP object GUID); null for local accounts.</summary>
    public string? ExternalId { get; set; }

    /// <summary>PBKDF2 hash (<see cref="Access.PasswordHashing"/>); null for directory accounts.</summary>
    public string? PasswordHash { get; set; }

    public UserKind Kind { get; set; }

    /// <summary>Every right in every library, and the server's own (accounts, libraries, server settings).</summary>
    public bool IsServerAdmin { get; set; }

    public bool Disabled { get; set; }

    /// <summary>Changes with the password or the account's status: sessions and links made before stop working.</summary>
    public string SecurityStamp { get; set; } = string.Empty;

    public int FailedSignIns { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime Created { get; set; }
    public DateTime? LastSignIn { get; set; }

    public List<GroupMember> Groups { get; set; } = [];
}

/// <summary>Guests never get the rights that change anything (<see cref="Access.RightDefinition.GuestAllowed"/>).</summary>
public enum UserKind
{
    User,
    Guest,
}
