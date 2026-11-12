using System.Text.Json.Serialization;

namespace Sfe.Core.Options;

[JsonConverter(typeof(JsonStringEnumConverter<KeepAliveMethod>))]
public enum KeepAliveMethod
{
    /// <summary>One directory query: reads only, but a client cache may answer it without asking the server.</summary>
    List,

    /// <summary>
    /// Writes and deletes a small file in the work folder at the library's root (<c>.sfe/keep-alive</c>), which always
    /// reaches the server. Reads instead when the work folder is on the server's side.
    /// </summary>
    Touch,
}
