namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// What folder rules leave out of an index listing, as conditions on its rows: the hidden children by key, or all
/// but the children a folder only leads to; the items a rule set elsewhere hides, by their provider's id.
/// </summary>
/// <remarks>
/// Names become keys as the index makes them (<see cref="RemoteIndexKeys.Key"/>). The route still checks each item
/// shown by its path and id (<see cref="ConnectionFolders.Seen"/>): a name the rules and the key fold apart is never
/// shown, whatever this filter missed.
/// </remarks>
internal sealed class IndexListingFilter
{
    private readonly List<string>? _only;
    private readonly List<string> _hidden;
    private readonly List<string> _hiddenIds;

    public IndexListingFilter(IndexListingQuery asked, string folderKey)
    {
        var caseInsensitive = asked.Target.CaseInsensitive;
        string KeyOf(string name) => RemotePaths.Combine(folderKey, RemoteIndexKeys.Key(name, caseInsensitive));
        _only = asked.Filter?.Only?.Select(KeyOf).Distinct().ToList();
        _hidden = asked.Filter?.Hidden.Select(KeyOf).Distinct().ToList() ?? [];
        _hiddenIds = [.. asked.HiddenIds];
    }

    /// <summary>What it leaves out, as a text: a later page goes on by key only for the same filter.</summary>
    public string Signature =>
        string.Join('\n', [.. (_only ?? []).Select(k => "o" + k), .. _hidden.Select(k => "h" + k), .. _hiddenIds]);

    public IQueryable<IndexListingRow> Apply(IQueryable<IndexListingRow> rows)
    {
        if (_only is { } only)
            rows = rows.Where(r => only.Contains(r.FullName));
        if (_hidden.Count > 0)
        {
            var hidden = _hidden;
            rows = rows.Where(r => !hidden.Contains(r.FullName));
        }
        if (_hiddenIds.Count > 0)
        {
            var ids = _hiddenIds;
            rows = rows.Where(r => r.ProviderId == null || !ids.Contains(r.ProviderId));
        }
        return rows;
    }
}
