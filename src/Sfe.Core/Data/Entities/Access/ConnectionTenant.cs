namespace Sfe.Core.Data;

/// <summary>A library a server-level <see cref="Connection"/> is offered to (those not offered to all).</summary>
public sealed class ConnectionTenant
{
    public int ConnectionId { get; set; }
    public Connection? Connection { get; set; }
    public int TenantId { get; set; }
}
