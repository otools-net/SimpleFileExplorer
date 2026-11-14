using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Core.KeepAlive;

/// <summary>What keep alive knows of one folder.</summary>
/// <param name="IsRoot">The library's folder itself (the others come from
/// <see cref="KeepAliveOptions.Folders"/>).</param>
/// <param name="IsNetwork">Null until the first ping looked.</param>
/// <param name="LastPing">When the last ping ended (null: none yet).</param>
/// <param name="Error">Why the last ping failed; null when it worked.</param>
/// <param name="FailingSince">The first failed ping of the current run of failures.</param>
/// <param name="Method">How the last ping was made; null: none yet.</param>
public sealed record KeepAliveFolder(
    string Path,
    bool IsRoot,
    bool? IsNetwork,
    DateTimeOffset? LastPing,
    TimeSpan? Duration,
    string? Error,
    DateTimeOffset? FailingSince,
    bool Waiting,
    KeepAliveMethod? Method
);
