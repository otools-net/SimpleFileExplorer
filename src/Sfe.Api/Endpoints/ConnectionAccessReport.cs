using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the asker may do in a connection (agents/docs/features/item-access.md): the listing's read-only reason and
/// Properties' Access section. SFE's reasons first (the server, the space, the account; then the connection: set
/// read-only, a folder rule, the groups' level), then the provider's (a folder it refuses changes in).
/// </summary>
internal sealed class ConnectionAccessReport(
    AccessState state,
    IReadOnlyCollection<string> serverOff,
    OpenConnection open
)
{
    /// <summary>
    /// Why SFE lets the asker change no file in folder <paramref name="path"/>, where it has <paramref name="here"/>;
    /// null when it lets it.
    /// </summary>
    public ReadOnlyReason? FolderReason(string path, FolderAccess here)
    {
        if (ListingAccess.SfeReason(state, serverOff, null) is { } inSpace)
            return inSpace;
        return ConnectionFolders.ChangesFiles(here) ? null : Why(path);
    }

    /// <summary>
    /// The access to <paramref name="item"/>, asked at <paramref name="path"/> (normalised);
    /// <paramref name="parentReadOnly"/>: the provider refuses changes in its folder.
    /// </summary>
    public ItemAccess Of(string path, RemoteItem item, bool parentReadOnly)
    {
        var actions = item.IsFolder
            ? FolderActions(path, item, parentReadOnly)
            : FileActions(path, item, parentReadOnly);
        var place = item.IsFolder ? item.ReadOnly : parentReadOnly;
        return new ItemAccess([.. actions], open.Folders.RuleFolder(path), Provider(place));
    }

    private IEnumerable<ActionAccess> FileActions(string path, RemoteItem item, bool parentReadOnly)
    {
        yield return Action(ItemAction.Open, Refusal(path, item, Rights.ViewFiles));
        yield return Action(ItemAction.Download, Refusal(path, item, Rights.Download));
        var saveOver =
            Refusal(path, item, Rights.DeleteFiles)
            ?? Refusal(RemotePaths.Parent(path), null, Rights.CreateFiles)
            ?? Provider(parentReadOnly || item.ReadOnly);
        yield return Action(ItemAction.SaveOver, saveOver);
        foreach (var (action, right) in Changes)
            yield return Action(action, Refusal(path, item, right) ?? Provider(parentReadOnly));
    }

    private IEnumerable<ActionAccess> FolderActions(string path, RemoteItem item, bool parentReadOnly)
    {
        yield return Action(ItemAction.Open, Refusal(path, item, Rights.Browse));
        yield return Action(ItemAction.Download, Refusal(path, item, Rights.Download));
        var inside = new[] { Rights.CreateFolders, Rights.CreateFiles, Rights.UploadFiles }
            .Select(right => Refusal(path, item, right))
            .ToList();
        yield return Action(ItemAction.CreateInside, inside.Any(r => r is null) ? Provider(item.ReadOnly) : inside[0]);
        if (path.Length == 0)
            yield break;
        foreach (var (action, right) in Changes)
            yield return Action(action, Refusal(path, item, right) ?? Provider(parentReadOnly));
    }

    /// <summary>What changes the item where it is: its folder must take changes.</summary>
    private static readonly (ItemAction Action, string Right)[] Changes =
    [
        (ItemAction.Rename, Rights.RenameFiles),
        (ItemAction.Move, Rights.MoveFiles),
        (ItemAction.Delete, Rights.DeleteFiles),
    ];

    private static ActionAccess Action(ItemAction action, ReadOnlyReason? refusal) =>
        new(action, refusal is null, refusal);

    private static ReadOnlyReason? Provider(bool readOnly) => readOnly ? ReadOnlyReason.Provider : null;

    /// <summary>
    /// Why SFE doesn't let the asker use <paramref name="right"/> at <paramref name="path"/> (on
    /// <paramref name="item"/> there, with the rules set on it at another path).
    /// </summary>
    private ReadOnlyReason? Refusal(string path, RemoteItem? item, string right)
    {
        if (!state.Has(right))
            return ItemAccessReport.Unheld(state, serverOff, right);
        var access = item is null ? open.Folders.At(path) : open.Folders.At(path, item.Id);
        return access.Has(right) ? null : Why(path);
    }

    /// <summary>
    /// Why the connection takes away a right the space gives: set read-only, else a folder rule at or above
    /// <paramref name="path"/>, else the level the asker's groups have.
    /// </summary>
    private ReadOnlyReason Why(string path)
    {
        if (open.Connection.ReadOnly)
            return ReadOnlyReason.Connection;
        return open.Folders.RuleFolder(path) is null ? ReadOnlyReason.Level : ReadOnlyReason.Rule;
    }
}
