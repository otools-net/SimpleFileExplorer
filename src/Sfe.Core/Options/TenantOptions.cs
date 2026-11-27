namespace Otools.Net.Sfe.Core.Options;

/// <summary>Libraries other than the first one (tenants).</summary>
public sealed class TenantOptions
{
    /// <summary>
    /// Folders the libraries' roots may be chosen in (with their sub-folders), used only while
    /// <see cref="UsesAllowedRoots"/>; otherwise new libraries are chosen inside <see cref="SfeOptions.Root"/>.
    /// </summary>
    public List<string> AllowedRoots { get; set; } = [];

    /// <summary>
    /// Whether <see cref="AllowedRoots"/> applies (off by default). Not set, it counts as on when the list is set:
    /// installs from before the switch keep their folders without editing their configuration.
    /// </summary>
    public bool? UseAllowedRoots { get; set; }

    /// <summary>
    /// Connection string of a new library's index, with <c>{tenant}</c> standing for the library's short name. When
    /// empty, SQLite servers keep each index in <c>data/tenants/{tenant}.db</c>; SQL Server needs one (e.g.
    /// <c>Server=.;Database=SFE_{tenant};Trusted_Connection=True;TrustServerCertificate=True</c>). The first library
    /// keeps <c>ConnectionStrings:Sfe</c>.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary><see cref="AllowedRoots"/> applies: switched on, or a list set with the switch left out.</summary>
    public bool UsesAllowedRoots => UseAllowedRoots ?? AllowedRoots.Count > 0;
}
