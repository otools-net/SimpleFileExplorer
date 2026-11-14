using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Connections;

namespace Sfe.Api.Endpoints;

/// <summary>
/// A connection opened for one request (<see cref="ConnectionFileServices.OpenAsync"/>), with where the session may
/// use its rights in it (<see cref="Folders"/>). Routes check their paths here before the provider is called: what
/// the session doesn't see answers 404, what it sees but may not do there 403.
/// </summary>
/// <param name="Folders">The session's folder access in the connection (<see cref="ConnectionPolicies"/>).</param>
internal sealed record OpenConnection(
    UsableConnection Connection,
    IRemoteStorage Storage,
    ProviderCapabilities Capabilities,
    ConnectionFolders Folders
)
{
    /// <summary>What the session may do at <paramref name="path"/> (normalised).</summary>
    public FolderAccess At(string path) => Folders.At(path);

    /// <summary>Whether the session may use <paramref name="right"/> at <paramref name="path"/> (normalised).</summary>
    public PathVerdict Check(string path, string right)
    {
        var access = Folders.At(path);
        if (access.Has(right))
            return PathVerdict.Allowed;
        return access.Has(Rights.Browse) ? PathVerdict.Forbidden : PathVerdict.NotFound;
    }

    /// <summary>
    /// <see cref="Check(string, string)"/> at the path asked and where the provider says the item is (another case,
    /// a name it folds): the stricter answer wins.
    /// </summary>
    public PathVerdict Check(string asked, RemoteItem item, string right)
    {
        var verdict = Check(asked, right);
        return item.Path == asked ? verdict : Stricter(verdict, Check(item.Path, right));
    }

    /// <summary>
    /// The session may use <paramref name="right"/> at <paramref name="path"/>, its folder and everything below it
    /// (a folder renamed, moved, deleted): 404 unseen, else 403 when anything there doesn't allow it.
    /// </summary>
    public PathVerdict CheckThroughout(string path, string right)
    {
        var verdict = Check(path, right);
        var everywhere = Folders.Allows(RemotePaths.Parent(path), right) && Folders.AllowsThroughout(path, right);
        return verdict == PathVerdict.Allowed && !everywhere ? PathVerdict.Forbidden : verdict;
    }

    /// <summary>The answer of a verdict that isn't <see cref="PathVerdict.Allowed"/>; null when it is.</summary>
    public static IResult? AnswerOf(PathVerdict verdict) =>
        verdict switch
        {
            PathVerdict.Allowed => null,
            PathVerdict.Forbidden => TypedResults.Forbid(),
            _ => TypedResults.NotFound(),
        };

    private static PathVerdict Stricter(PathVerdict one, PathVerdict other) => one > other ? one : other;
}
