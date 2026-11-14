using Sfe.Core.Library;

namespace Sfe.Core.Connections;

/// <summary>The listing order of <see cref="LibraryBrowser"/> for a connection's items: folders first.</summary>
public static class RemoteSort
{
    public static List<RemoteItem> Sort(IEnumerable<RemoteItem> items, SortField sort, bool descending)
    {
        var ordered = items.OrderBy(i => i.IsFolder ? 0 : 1);
        var sorted = (sort, descending) switch
        {
            (SortField.Modified, false) => ordered.ThenBy(i => i.Modified),
            (SortField.Modified, true) => ordered.ThenByDescending(i => i.Modified),
            (SortField.Size, false) => ordered.ThenBy(i => i.Size),
            (SortField.Size, true) => ordered.ThenByDescending(i => i.Size),
            (SortField.Type, false) => ordered.ThenBy(ExtensionOf, StringComparer.OrdinalIgnoreCase),
            (SortField.Type, true) => ordered.ThenByDescending(ExtensionOf, StringComparer.OrdinalIgnoreCase),
            (_, true) => ordered.ThenByDescending(i => i.Name, NaturalComparer.Instance),
            _ => ordered,
        };
        return [.. sorted.ThenBy(i => i.Name, NaturalComparer.Instance)];
    }

    /// <summary>The extension as the library lists it (lower case, no dot); null for a folder or none.</summary>
    public static string? ExtensionOf(RemoteItem item) =>
        item.IsFolder ? null : LibraryBrowser.NormalizeExtension(System.IO.Path.GetExtension(item.Name));
}
