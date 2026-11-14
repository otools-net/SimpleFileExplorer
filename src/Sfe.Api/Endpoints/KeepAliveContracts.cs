using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <param name="Path">Relative to the library root; empty for the root itself.</param>
/// <param name="IsRoot">The library folder itself; the others are the more folders of the settings.</param>
/// <param name="IsNetwork">Whether it is on a network share; null until the first ping looked.</param>
/// <param name="LastPing">When the last ping ended; null: none yet.</param>
/// <param name="DurationMilliseconds">How long the last ping took, when it worked.</param>
/// <param name="Error">Why the last ping failed; null when it worked. The library's place on the server is not
/// named.</param>
/// <param name="FailingSince">The first failed ping of the current run of failures.</param>
/// <param name="Waiting">A ping is still blocked on the folder (the share does not answer).</param>
/// <param name="Method">How the last ping was made (a folder read, or a file written in the work folder); null: none
/// yet.</param>
public sealed record KeepAliveFolderView(
    string Path,
    bool IsRoot,
    bool? IsNetwork,
    DateTimeOffset? LastPing,
    double? DurationMilliseconds,
    string? Error,
    DateTimeOffset? FailingSince,
    bool Waiting,
    KeepAliveMethod? Method
);

/// <param name="Mode">The mode in effect for the space.</param>
/// <param name="Method">How the library folder is to be pinged (the more folders are always read).</param>
/// <param name="Folders">The folders pinged so far, since the server started: the library folder first.</param>
public sealed record KeepAliveView(
    KeepAliveMode Mode,
    int IntervalSeconds,
    KeepAliveMethod Method,
    IReadOnlyList<KeepAliveFolderView> Folders
);
