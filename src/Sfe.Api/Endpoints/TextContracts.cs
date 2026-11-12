namespace Sfe.Api.Endpoints;

/// <summary>
/// What the caller may do with a text file (from the server: the client's own idea is optimistic in searches).
/// </summary>
/// <param name="Overwrite">Save over it: may delete it (to the trash, or for good in a space that always deletes for
/// good) and create files in its folder, as move/copy's Replace.</param>
/// <param name="SaveCopy">Save a copy in its folder (<c>files.createFiles</c> there).</param>
/// <param name="Download">Download the edited text (<c>files.download</c>).</param>
/// <param name="Permanent">The space deletes for good: no previous version is kept in the trash.</param>
public sealed record TextAccess(bool Overwrite, bool SaveCopy, bool Download, bool Permanent);

/// <param name="Text">The content, line endings as they are in the file.</param>
/// <param name="Encoding">How it was read: <c>utf-8</c>, <c>utf-8-bom</c>, <c>utf-16le</c>, <c>utf-16be</c>,
/// <c>windows-1252</c>.</param>
/// <param name="LineEnding"><c>lf</c>, <c>crlf</c>, <c>cr</c> or <c>mixed</c>.</param>
/// <param name="Version">The content's version (also the ETag): what a save will say it changes.</param>
/// <param name="SaveLineEnding">What a save writes: the line ending, or the most used one of a file that mixes
/// them.</param>
public sealed record TextFile(
    string Text,
    string Encoding,
    string LineEnding,
    string Version,
    long Size,
    DateTime Modified,
    TextAccess? Access = null,
    string? SaveLineEnding = null
);

/// <param name="Version">Null for a file larger than can be read whole (it is read in parts).</param>
public sealed record TextVersion(string? Version, long Size, DateTime Modified);

/// <param name="Session">The editing session (letters, digits, dashes; made by the client when the editor opens): the
/// previous version is kept in the trash at its first save only.</param>
public sealed record SaveTextRequest(string? Text, string? Encoding, string? LineEnding, string? Session);

/// <param name="TrashId">The previous version's trash entry, when this save kept one.</param>
public sealed record SavedText(string Version, long Size, DateTime Modified, int? TrashId);

/// <param name="Path">The video a subtitle file belongs to (root-relative), or null when there is none the caller
/// sees.</param>
public sealed record SubtitleVideo(string? Path);

/// <param name="Path">The folder the copy goes in.</param>
public sealed record CopyTextRequest(string? Path, string? Name, string? Text, string? Encoding, string? LineEnding);
