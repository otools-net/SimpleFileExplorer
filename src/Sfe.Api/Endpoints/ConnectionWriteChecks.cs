using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Files;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The folder rules of a connection on the routes changing its files, checked before the provider is called: the
/// item and its folder allow the change (all below it for a folder), the folder something goes into allows it, and a
/// name hidden from the session counts as taken (409, whatever is there), so nothing is written over what it can't
/// see.
/// </summary>
internal static class ConnectionWriteChecks
{
    /// <summary>
    /// The answer when the session may not make the item <paramref name="path"/> in <paramref name="folder"/> with
    /// <paramref name="right"/>; null when it may.
    /// </summary>
    public static IResult? NewItem(OpenConnection open, string folder, string path, string right)
    {
        if (OpenConnection.AnswerOf(open.Check(folder, right)) is { } refused)
            return refused;
        if (!open.At(path).IsVisible)
            return Exists(RemotePaths.NameOf(path));
        return OpenConnection.AnswerOf(open.Check(path, right));
    }

    /// <summary>The 409 of a name already taken (or hidden from the session).</summary>
    public static IResult Exists(string name) =>
        TypedResults.Problem(
            $"“{name}” is already there.",
            statusCode: StatusCodes.Status409Conflict,
            type: FileEndpoints.ExistsType
        );

    /// <summary>
    /// What stops an item of a move or copy at its place: a move takes it away (the right at it, its folder and all
    /// below it); a copy reads all of it (<c>files.view</c> throughout), so a hidden folder inside is never copied
    /// where others see it. Null when nothing does.
    /// </summary>
    public static FileOperationStatus? SourceStatus(OpenConnection open, string path, bool move)
    {
        if (move)
            return StatusOf(open.CheckThroughout(path, Rights.MoveFiles));
        if (StatusOf(open.Check(path, Rights.ViewFiles)) is { } status)
            return status;
        return open.Folders.AllowsThroughout(path, Rights.ViewFiles) ? null : FileOperationStatus.Forbidden;
    }

    /// <summary>
    /// What stops an item from arriving at <paramref name="path"/> with <paramref name="right"/>: a name hidden from
    /// the session is taken; null when nothing does.
    /// </summary>
    public static FileOperationStatus? TargetStatus(OpenConnection open, string path, string right)
    {
        if (!open.At(path).IsVisible)
            return FileOperationStatus.Exists;
        return StatusOf(open.Check(path, right));
    }

    /// <summary>An item's status for a verdict; null when allowed.</summary>
    public static FileOperationStatus? StatusOf(PathVerdict verdict) =>
        verdict switch
        {
            PathVerdict.Allowed => null,
            PathVerdict.Forbidden => FileOperationStatus.Forbidden,
            _ => FileOperationStatus.NotFound,
        };
}
