namespace Otools.Net.Sfe.Core.Library.Watching;

/// <summary>How folders are watched (<see cref="FolderWatchers"/>); shorter in tests.</summary>
/// <param name="Settle">Changes close together (a program saving through a temporary file) make one notice.</param>
/// <param name="MinGap">The shortest time between two notices of a folder (a file written for minutes).</param>
/// <param name="PollEvery">How often a folder no watcher can follow is read again, at the least.</param>
/// <param name="OwnChangeWindow">
/// A change SFE made (told by <see cref="LibraryBrowser.FolderChanged"/>) this close to the watcher's event: the
/// event is SFE's own change seen on the disk, already told.
/// </param>
public sealed record FolderWatchTimings(
    TimeSpan Settle,
    TimeSpan MinGap,
    TimeSpan PollEvery,
    TimeSpan OwnChangeWindow
)
{
    public static readonly FolderWatchTimings Default = new(
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(1)
    );
}
