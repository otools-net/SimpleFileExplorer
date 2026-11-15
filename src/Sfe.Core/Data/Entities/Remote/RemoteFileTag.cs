namespace Otools.Net.Sfe.Core.Data;

/// <summary>A tag of the library on a file of a connection (<see cref="RemoteFile"/>).</summary>
public sealed class RemoteFileTag
{
    public long FileId { get; set; }
    public RemoteFile? File { get; set; }
    public int TagId { get; set; }
    public Tag? Tag { get; set; }
}
