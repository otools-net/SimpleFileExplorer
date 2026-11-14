using Otools.Net.Sfe.Core.Connections;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The provider's ids of the folders rules are set on, looked up when a library sets its rules in a connection (the
/// rule then also applies where the folder goes when renamed or moved outside SFE). Best effort: a connection that
/// can't be reached, a folder not found, a provider without ids, or more than <see cref="MaxLookups"/> new rules
/// leave the rules without an id; they still apply by path.
/// </summary>
internal static class ConnectionRuleIds
{
    /// <summary>Folders looked up in one change (each is a call to the provider).</summary>
    public const int MaxLookups = 50;

    /// <summary><paramref name="rules"/>, those without an id given the id of their folder when found.</summary>
    public static async Task<IReadOnlyList<ConnectionRuleInput>> FillAsync(
        int id,
        IReadOnlyList<ConnectionRuleInput> rules,
        (ConnectionService Connections, ConnectionGates Gates) services,
        CancellationToken cancellationToken
    )
    {
        var paths = rules
            .Where(r => r.ItemId is null)
            .Select(r => RemotePaths.Normalize(r.Path))
            .OfType<string>()
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxLookups)
            .ToList();
        if (paths.Count == 0 || services.Gates.WaitLeft(id) is not null)
            return rules;
        var found = await LookUpAsync(id, paths, services, cancellationToken);
        return
        [
            .. rules.Select(r =>
                r.ItemId is null && RemotePaths.Normalize(r.Path) is { } path && found.TryGetValue(path, out var itemId)
                    ? r with
                    {
                        ItemId = itemId,
                    }
                    : r
            ),
        ];
    }

    private static async Task<Dictionary<string, string>> LookUpAsync(
        int id,
        List<string> paths,
        (ConnectionService Connections, ConnectionGates Gates) services,
        CancellationToken cancellationToken
    )
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        var opened = await services.Connections.OpenAsync(id, cancellationToken);
        if (opened.Value is not { } storage)
            return found;
        await using (storage)
        {
            using var turn = await services.Gates.EnterAsync(id, cancellationToken);
            if (turn is null)
                return found;
            try
            {
                foreach (var path in paths)
                {
                    if (
                        await storage.GetAsync(path, cancellationToken) is { IsFolder: true, Id: { } itemId }
                        && Fits(itemId)
                    )
                        found[path] = itemId;
                }
            }
            catch (RemoteStorageException error)
            {
                if (error.Kind == RemoteErrorKind.RateLimited)
                    services.Gates.Wait(id, error.RetryAfter);
            }
        }
        return found;
    }

    private static bool Fits(string itemId) =>
        itemId.Length is > 0 and <= Otools.Net.Sfe.Core.Data.ConnectionFolderRule.MaxItemIdLength;
}
