namespace Sfe.Core.Data;

/// <summary>
/// A folder or file of a library that a user keeps at hand in the sidebar (Quick links). Kept by path, as the user
/// saw it: a link whose target went away stays, shown as missing, until the user removes it.
/// </summary>
public sealed class QuickLink
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int TenantId { get; set; }

    /// <summary>Relative to the library root, '/' separated; empty for the root itself.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>A folder (else a file), as it was when added.</summary>
    public bool IsFolder { get; set; }

    /// <summary>Order in the user's list, from 0.</summary>
    public int Position { get; set; }
}
