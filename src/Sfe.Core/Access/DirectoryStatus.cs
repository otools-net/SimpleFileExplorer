namespace Otools.Net.Sfe.Core.Access;

public enum DirectoryStatus
{
    Ok,

    /// <summary>The account is not in this directory: ask the next one.</summary>
    Unknown,

    /// <summary>The account is there but the password is wrong.</summary>
    Invalid,

    /// <summary>Too many failed sign-ins: refused until <see cref="DirectoryResult.LockedUntil"/>.</summary>
    LockedOut,
}
