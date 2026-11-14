namespace Otools.Net.Sfe.Core.Options;

/// <summary>Libraries other than the first one (tenants).</summary>
public sealed class TenantOptions
{
    /// <summary>
    /// Folders the libraries' roots may be chosen in (with their sub-folders); empty = <see cref="SfeOptions.Root"/>.
    /// </summary>
    public List<string> AllowedRoots { get; set; } = [];

    /// <summary>
    /// Connection string of a new library's index, with <c>{tenant}</c> standing for the library's short name. When
    /// empty, SQLite servers keep each index in <c>data/tenants/{tenant}.db</c>; SQL Server needs one (e.g.
    /// <c>Server=.;Database=SFE_{tenant};Trusted_Connection=True;TrustServerCertificate=True</c>). The first library
    /// keeps <c>ConnectionStrings:Sfe</c>.
    /// </summary>
    public string? ConnectionString { get; set; }
}
