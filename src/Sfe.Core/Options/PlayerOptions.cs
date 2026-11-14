using System.Text.RegularExpressions;

namespace Otools.Net.Sfe.Core.Options;

/// <summary>The web client's video player.</summary>
public sealed partial class PlayerOptions
{
    /// <summary>
    /// Seconds jumped by the back/forward buttons, the J/L keys and a double tap on a side of the picture.
    /// </summary>
    public int SeekSeconds { get; set; } = 10;

    /// <summary>Videos start over at their end; the player's loop button switches it for the video on screen.</summary>
    public bool Loop { get; set; }

    /// <summary>Name of the external player on its button (e.g. "Open in VLC").</summary>
    public string? ExternalName { get; set; } = "VLC";

    /// <summary>
    /// Link opening a file in an external player, handled by the app that registered its scheme (<c>vlc://</c>,
    /// <c>potplayer://</c>, <c>infuse://</c>, an Android <c>intent:</c>…). <c>{url}</c> stands for the file's full
    /// address, <c>{encodedUrl}</c> for the same address percent-encoded (for a query string); without either the
    /// address is appended. Empty = no external player button.
    /// </summary>
    public string? ExternalUrl { get; set; } = "vlc://{url}";

    /// <summary>
    /// Minutes a link given to an external player works. The player fetches the file itself, without the browser's
    /// sign-in, so the link carries a token of its own; it must outlast a whole film, as players ask again on each
    /// seek.
    /// </summary>
    public int LinkLifetimeMinutes { get; set; } = 360;

    /// <summary>
    /// Flash movies (<c>.swf</c>) play in the viewer, through Ruffle (a Flash Player emulator bundled with the app), in
    /// a frame that can't act as the signed-in user. Off: they are only downloaded.
    /// </summary>
    public bool Flash { get; set; } = true;

    /// <summary>
    /// The link starts with a scheme other than those running script or showing content in the page itself: a library's
    /// administrator sets it for everyone, so a <c>javascript:</c> link would run in the browser of every user and
    /// guest.
    /// </summary>
    public static bool IsAllowedExternalUrl(string url)
    {
        var scheme = SchemeRegex().Match(url);
        return scheme.Success && !BlockedSchemes.Contains(scheme.Groups[1].Value);
    }

    private static readonly HashSet<string> BlockedSchemes = new(
        ["javascript", "vbscript", "data", "blob", "file", "about"],
        StringComparer.OrdinalIgnoreCase
    );

    [GeneratedRegex(@"^([a-zA-Z][a-zA-Z0-9+.-]*):")]
    private static partial Regex SchemeRegex();
}
