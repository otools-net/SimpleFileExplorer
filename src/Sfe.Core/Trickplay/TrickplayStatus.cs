using Sfe.Core.Options;

namespace Sfe.Core.Trickplay;

/// <param name="Checked">Videos looked at by the current (or last) pass over the folders.</param>
/// <param name="Generated">Videos given trickplay by that pass and by the player's requests since it started.</param>
/// <param name="Failed">Videos ffmpeg couldn't handle; tried again when they or the settings change.</param>
/// <param name="Queued">Videos opened in the player, waiting for their trickplay.</param>
/// <param name="Pending">Videos the current pass found without (current) trickplay, waiting for their turn.</param>
/// <param name="Discovering">The current pass is still looking for videos: <see cref="Pending"/> may grow.</param>
/// <param name="Current">Videos being processed.</param>
/// <param name="LastPassFinished">When the last complete pass over the folders ended.</param>
/// <param name="PassStarted">When the pass running started; null between passes.</param>
/// <param name="NextPass">When the next periodic pass starts (<see cref="TrickplayOptions.RecheckHours"/>); null when
/// none is planned.</param>
public sealed record TrickplayStatus(
    TrickplayState State,
    int Checked,
    int Generated,
    int Failed,
    int Queued,
    int Pending,
    bool Discovering,
    IReadOnlyList<string> Current,
    DateTimeOffset? LastPassFinished,
    DateTimeOffset? PassStarted = null,
    DateTimeOffset? NextPass = null
);
