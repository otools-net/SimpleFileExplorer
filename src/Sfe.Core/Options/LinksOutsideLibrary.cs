namespace Otools.Net.Sfe.Core.Options;

/// <summary>
/// What SFE does with a link in a library (symbolic link, junction) that leads outside it
/// (<see cref="SecurityOptions.LinksOutsideLibrary"/>).
/// </summary>
public enum LinksOutsideLibrary
{
    /// <summary>Followed, to read and to write: a home library adding a drive through a link.</summary>
    Follow,

    /// <summary>
    /// Followed to browse, play and download; nothing is created, changed, renamed, moved away or deleted behind it.
    /// For a library other people can put links in outside SFE (a network share, SFTP, a shared computer).
    /// </summary>
    ReadOnly,
}
