namespace Sfe.Core.Data;

/// <summary>
/// A library: its own root folder, index, tags, schedules, caches and settings, and its own groups of users. The
/// first one (<see cref="IsDefault"/>) is the library of the configuration (<c>Sfe:Root</c>, <c>ConnectionStrings:Sfe</c>).
/// </summary>
public sealed class Tenant
{
    public int Id { get; set; }

    /// <summary>Short name, unique: names the library's index and cache folders, and comes first in its links.</summary>
    public string Slug { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Full path of the root folder; null = <c>Sfe:Root</c> (the first library).</summary>
    public string? Root { get; set; }

    /// <summary>Connection string of the index; null = <c>ConnectionStrings:Sfe</c> or <c>Sfe:Tenants:ConnectionString</c>.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Visitors without an account may browse it, with the rights of its Guests group.</summary>
    public bool GuestAccess { get; set; }

    /// <summary>The library of the configuration, created on the first start: it cannot be deleted.</summary>
    public bool IsDefault { get; set; }

    public bool Disabled { get; set; }
    public DateTime Created { get; set; }

    /// <summary>
    /// The revision of the rights catalog its built-in groups were given (<see cref="Access.RightCatalog.Revision"/>):
    /// rights added since are granted once at start-up. Null = the spaces created by 1.34 (revision 0).
    /// </summary>
    public int? RightsRevision { get; set; }

    public List<UserGroup> Groups { get; set; } = [];
    public List<TenantDisabledRight> DisabledRights { get; set; } = [];
}
