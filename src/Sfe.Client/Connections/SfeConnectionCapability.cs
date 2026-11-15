namespace Otools.Net.Sfe.Client;

/// <summary>
/// What a call may need of a connection's provider (<see cref="SfeConnectionCapabilities"/>); one it lacks fails with
/// <see cref="SfeNotSupportedException"/> before the call.
/// </summary>
public enum SfeConnectionCapability
{
    /// <summary>A part of a file read (an offset, a length, a download going on after a cut-off).</summary>
    RangedReads = 1,

    /// <summary>Uploads, sent in parts.</summary>
    UploadsInParts,

    /// <summary>Copies inside the connection, done by the provider.</summary>
    ServerSideCopy,

    /// <summary>Moves inside the connection, done by the provider.</summary>
    ServerSideMove,
}
