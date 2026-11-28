namespace Otools.Net.Sfe.Core.Data;

/// <summary>
/// The folder of a library an account chose as its home there (the sidebar's Home), over the default of its groups
/// (<see cref="UserGroup.HomeFolder"/>). Kept by path: a folder that went away stays chosen, shown as missing.
/// </summary>
public sealed class HomeFolder
{
    public int UserId { get; set; }
    public int TenantId { get; set; }

    /// <summary>Relative to the library root, '/' separated; empty for the root itself.</summary>
    public string Path { get; set; } = string.Empty;
}
