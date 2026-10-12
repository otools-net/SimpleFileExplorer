using Sfe.Core.Data;

namespace Sfe.Core.Access;

/// <param name="Root">Full path of the root folder; null for the first library (it follows <c>Sfe:Root</c>).</param>
/// <param name="DisabledRights">Rights nobody gets in this library (<see cref="TenantDisabledRight"/>).</param>
/// <param name="ConnectionString">Its index's own connection string; null = the server's (never sent to the web client).</param>
public sealed record TenantInfo(int Id, string Slug, string Name, bool GuestAccess, bool IsDefault, bool Disabled, string? Root, IReadOnlyList<string> DisabledRights,
    string? ConnectionString = null);

public sealed record MembershipInfo(int TenantId, int GroupId, string Group);

/// <param name="Source"><c>local</c>, or the directory the account comes from (its password is checked there).</param>
/// <param name="LockedOut">Too many failed sign-ins: locked until a while has passed or an administrator unlocks it.</param>
public sealed record UserInfo(
    int Id, string UserName, string? DisplayName, string Source, UserKind Kind, bool IsServerAdmin, bool Disabled, bool LockedOut,
    DateTime Created, DateTime? LastSignIn, IReadOnlyList<MembershipInfo> Groups);

public sealed record GroupInfo(int Id, int TenantId, string Name, BuiltInGroup? BuiltIn, string? ExternalName, IReadOnlyList<string> Rights, IReadOnlyList<int> MemberIds);

/// <param name="Password">Required for a new local account; null keeps the current one.</param>
/// <param name="GroupIds">The account's groups, replacing those it had in the libraries the change covers; null keeps them.</param>
/// <param name="Unlock">Clears a lock left by failed sign-ins.</param>
public sealed record UserInput(
    string? UserName, string? DisplayName = null, string? Password = null, UserKind Kind = UserKind.User, bool IsServerAdmin = false,
    bool Disabled = false, IReadOnlyList<int>? GroupIds = null, bool Unlock = false);

/// <param name="Rights">The group's rights, replacing its current ones; null keeps them.</param>
/// <param name="MemberIds">The group's members, replacing its current ones; null keeps them.</param>
public sealed record GroupInput(string? Name, string? ExternalName = null, IReadOnlyList<string>? Rights = null, IReadOnlyList<int>? MemberIds = null);

/// <param name="Slug">Short name for its index and caches; made from the name when empty. Fixed once created.</param>
/// <param name="Root">Root folder (full path, inside <c>Sfe:Tenants:AllowedRoots</c>); ignored for the first library.</param>
/// <param name="DisabledRights">Rights nobody gets in this library; null keeps the current ones.</param>
public sealed record TenantInput(string? Name, string? Slug = null, string? Root = null, bool GuestAccess = false, bool Disabled = false,
    IReadOnlyList<string>? DisabledRights = null);

/// <summary>What a request needs to know about the signed-in account (cached, see <see cref="AccessService.GetSessionUserAsync"/>).</summary>
public sealed record SessionUser(int Id, string UserName, string? DisplayName, string Source, UserKind Kind, bool IsServerAdmin, bool Disabled, string SecurityStamp)
{
    public bool IsGuest => Kind == UserKind.Guest;
    public bool IsLocal => Source == UserAccount.LocalSource;
}

public enum AccessStatus
{
    Ok,
    Invalid,
    NotFound,

    /// <summary>The change would break something (the last administrator, a built-in group...): see the problem.</summary>
    Conflict,
}

public sealed record AccessResult<T>(AccessStatus Status, T? Value = default, Dictionary<string, string[]>? Errors = null, string? Problem = null)
{
    public static AccessResult<T> Ok(T value) => new(AccessStatus.Ok, value);
    public static AccessResult<T> Invalid(string key, string message) => new(AccessStatus.Invalid, Errors: new() { [key] = [message] });
    public static AccessResult<T> Missing() => new(AccessStatus.NotFound);
    public static AccessResult<T> Conflict(string problem) => new(AccessStatus.Conflict, Problem: problem);

    /// <summary>The same failure, for another kind of value.</summary>
    public AccessResult<TOther> As<TOther>() => new(Status, default, Errors, Problem);
}
