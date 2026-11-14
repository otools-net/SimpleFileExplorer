using System.Text.Json.Serialization;

namespace Otools.Net.Sfe.Core.Options;

[JsonConverter(typeof(JsonStringEnumConverter<KeepAliveMode>))]
public enum KeepAliveMode
{
    Off,

    /// <summary>
    /// Only folders on a network share (a UNC path, a mapped drive, a CIFS/NFS/SSHFS mount), links followed.
    /// </summary>
    Auto,

    /// <summary>Every folder, also one SFE can't tell is on the network.</summary>
    Always,
}
