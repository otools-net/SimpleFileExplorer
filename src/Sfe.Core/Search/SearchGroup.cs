using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Core.Search;

/// <param name="ExcludedFilters"> One group per negated size, extension, resolution, duration or type term (<c>-s:</c>,
/// <c>-e:</c>, <c>-w:</c>, <c>-h:</c>,
/// <c>-r:</c>, <c>-d:</c>, <c>-type:</c>): files matching it are left out.
/// </param>
/// <param name="Levels">High or low resolution terms (<c>r:hr</c>, <c>r:lr</c>).</param>
/// <param name="Kinds">Kinds of file (<c>type:</c>), alternatives; <see cref="ItemKind.File"/> = none of the media
/// kinds.</param>
public sealed record SearchGroup(
    IReadOnlyList<string> Include,
    IReadOnlyList<string> Exclude,
    IReadOnlyList<SizeFilter> Sizes,
    IReadOnlyList<string> Extensions,
    IReadOnlyList<TagFilter> Tags,
    IReadOnlyList<DimensionFilter> Dimensions,
    IReadOnlyList<SearchGroup> ExcludedFilters,
    IReadOnlyList<DurationFilter> Durations,
    IReadOnlyList<ResolutionLevel> Levels,
    IReadOnlyList<ItemKind> Kinds
)
{
    public bool IsEmpty =>
        Include.Count == 0
        && Exclude.Count == 0
        && Sizes.Count == 0
        && Extensions.Count == 0
        && Tags.Count == 0
        && Dimensions.Count == 0
        && ExcludedFilters.Count == 0
        && Durations.Count == 0
        && Levels.Count == 0
        && Kinds.Count == 0;
}
