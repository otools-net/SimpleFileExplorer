namespace Sfe.Core.Data;

/// <summary>A right nobody gets in this library, whatever its groups grant (set by server administrators).</summary>
public sealed class TenantDisabledRight
{
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public string Right { get; set; } = string.Empty;
}
