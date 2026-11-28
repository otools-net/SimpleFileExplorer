namespace Otools.Net.Sfe.Core.Data;

/// <summary>Users of a library with the same rights. Each library has the three <see cref="BuiltInGroup"/>s.</summary>
public sealed class UserGroup
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Set for the built-in groups, which cannot be deleted; null for groups made by hand.</summary>
    public BuiltInGroup? BuiltIn { get; set; }

    /// <summary>
    /// Name of a directory group (e.g. an LDAP group) whose members belong to this one: synchronised at each sign-in
    /// of a directory account. Null = members added by hand.
    /// </summary>
    public string? ExternalName { get; set; }

    /// <summary>
    /// The home folder of its members who chose none (<see cref="Data.HomeFolder"/>), relative to the library root;
    /// <c>{user}</c> in it stands for the account's name. Null = none.
    /// </summary>
    public string? HomeFolder { get; set; }

    public List<GroupMember> Members { get; set; } = [];
    public List<GroupRight> Rights { get; set; } = [];
}
