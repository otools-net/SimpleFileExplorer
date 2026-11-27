using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the asker may do with one file or folder and what the disk allows there, for Properties' Access section
/// (agents/docs/features/item-access.md): each action with the first reason refusing it, SFE before the disk.
/// </summary>
internal sealed class ItemAccessReport(
    AccessState state,
    LibraryPaths paths,
    IReadOnlyCollection<string> serverOff,
    ServerFiles serverFiles,
    WorkFolder work
)
{
    private readonly Dictionary<string, ReadOnlyReason?> _places = new(LibraryPaths.PathComparer);

    /// <summary>
    /// The access to <paramref name="path"/> (canonical, root-relative), described by the disk as
    /// <paramref name="details"/>.
    /// </summary>
    public ItemAccess Of(string path, ItemDetails details)
    {
        var parent = path.Length == 0 ? null : path[..Math.Max(path.LastIndexOf('/'), 0)];
        var actions = details.IsFolder ? FolderActions(path, parent) : FileActions(path, parent!, details.ReadOnly);
        var ruleFolder = state.Access.FolderPolicy.RuleFolder(path);
        return new ItemAccess([.. actions], ruleFolder, Place(details.IsFolder ? path : parent!));
    }

    private IEnumerable<ActionAccess> FileActions(string path, string parent, bool fileReadOnly)
    {
        yield return Action(ItemAction.Open, Refusal(path, Rights.ViewFiles));
        yield return Action(ItemAction.Download, Refusal(path, Rights.Download));
        var saveOver = Refusal(path, Rights.DeleteFiles) ?? Refusal(parent, Rights.CreateFiles) ?? Place(parent);
        yield return Action(ItemAction.SaveOver, saveOver ?? (fileReadOnly ? ReadOnlyReason.FileReadOnly : null));
        foreach (var write in Changes)
            yield return Action(write.Action, Refusal(path, write.Right) ?? Place(parent));
    }

    private IEnumerable<ActionAccess> FolderActions(string path, string? parent)
    {
        yield return Action(ItemAction.Open, Refusal(path, Rights.Browse));
        yield return Action(ItemAction.Download, Refusal(path, Rights.Download));
        var inside = new[] { Rights.CreateFolders, Rights.CreateFiles, Rights.UploadFiles }
            .Select(right => Refusal(path, right))
            .ToList();
        yield return Action(ItemAction.CreateInside, inside.Any(r => r is null) ? Place(path) : inside[0]);
        if (parent is null)
            yield break;
        foreach (var write in Changes)
            yield return Action(write.Action, Refusal(path, write.Right) ?? Place(parent));
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

    /// <summary>Why SFE doesn't let the asker use <paramref name="right"/> at <paramref name="path"/>.</summary>
    private ReadOnlyReason? Refusal(string path, string right)
    {
        if (state.Has(right))
            return state.At(paths, path) is { } here && !here.Has(right) ? ReadOnlyReason.Rule : null;
        return Unheld(state, serverOff, right);
    }

    /// <summary>Why the asker doesn't hold <paramref name="right"/> in the space (it doesn't).</summary>
    internal static ReadOnlyReason Unheld(AccessState state, IReadOnlyCollection<string> serverOff, string right)
    {
        if (RightCatalog.MatchesAny(serverOff, right))
            return ReadOnlyReason.Server;
        if (state.Access.SpaceDisabled?.Contains(right) == true)
            return ReadOnlyReason.Space;
        return state.Access.IsGuest && RightCatalog.Find(right) is { GuestAllowed: false }
            ? ReadOnlyReason.Visitor
            : ReadOnlyReason.Rights;
    }

    /// <summary>Why the folder <paramref name="path"/> takes no change on the disk (asked once per folder).</summary>
    private ReadOnlyReason? Place(string path)
    {
        if (!_places.TryGetValue(path, out var reason))
        {
            reason = paths.TryResolve(path, out var full) ? ListingAccess.DiskReason(full, serverFiles, work) : null;
            _places[path] = reason;
        }
        return reason;
    }
}
