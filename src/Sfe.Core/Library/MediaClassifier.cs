using Microsoft.Extensions.Options;
using Sfe.Core.Options;

namespace Sfe.Core.Library;

public sealed class MediaClassifier(IOptionsMonitor<SfeOptions> options)
{
    // Rebuilt when the file type lists change (a new options instance comes with new lists). A class, so that
    // concurrent requests swap it with one atomic reference write.
    private sealed record Cache(FileTypeOptions Source, Dictionary<string, ItemKind> Kinds);
    private Cache? _cache;

    /// <param name="extension">Extension with or without the leading dot.</param>
    public ItemKind Classify(string? extension) =>
        !string.IsNullOrEmpty(extension) && Kinds().TryGetValue(extension.TrimStart('.'), out var kind)
            ? kind
            : ItemKind.File;

    /// <summary>The extensions (without dot) classified as <paramref name="kind"/>; one listed under two kinds counts as the last.</summary>
    public IReadOnlyList<string> Extensions(ItemKind kind) => [.. Kinds().Where(k => k.Value == kind).Select(k => k.Key)];

    private Dictionary<string, ItemKind> Kinds()
    {
        var fileTypes = options.CurrentValue.FileTypes;
        if (_cache is { } cache && ReferenceEquals(cache.Source, fileTypes))
            return cache.Kinds;

        var kinds = new Dictionary<string, ItemKind>(StringComparer.OrdinalIgnoreCase);
        Register(kinds, fileTypes.Audio, ItemKind.Audio);
        Register(kinds, fileTypes.Images, ItemKind.Image);
        Register(kinds, fileTypes.Videos, ItemKind.Video);
        _cache = new Cache(fileTypes, kinds);
        return kinds;
    }

    private static void Register(Dictionary<string, ItemKind> kinds, IEnumerable<string> extensions, ItemKind kind)
    {
        foreach (var extension in extensions)
            kinds[extension.TrimStart('.')] = kind;
    }
}
