namespace Otools.Net.Sfe.Client;

/// <summary>
/// A function a server may lack (older, turned off, or not for this account), checked with
/// <see cref="SfeServerInfo.Supports"/> or <see cref="SfeServer.RequireAsync"/>.
/// </summary>
public enum SfeFunction
{
    /// <summary>Previews of pictures and videos (needs ffmpeg on the server for videos).</summary>
    Thumbnails = 1,

    /// <summary>Timeline sheets of videos.</summary>
    Trickplay,

    /// <summary>External storage (connections) this account may use or manage.</summary>
    Connections,

    /// <summary>Personal access tokens, switched on, for an account that may have them.</summary>
    AccessTokens,
}
