using System.Globalization;
using System.Text.RegularExpressions;
using Sfe.Core.Library;

namespace Sfe.Core.Search;

/// <summary>
/// Parsed search expression. Syntax:
/// <list type="bullet">
/// <item><c>,</c> separates alternatives (OR).</item>
/// <item>a space separates terms that must all match (AND).</item>
/// <item><c>-term</c> excludes names containing <c>term</c>. A <c>-</c> before any other term (<c>-s:</c>, <c>-e:</c>,
/// <c>-w:</c>, <c>-h:</c>, <c>-r:</c>, <c>-d:</c>, <c>-type:</c>, <c>-t:</c>, <c>-special:untagged</c>) finds exactly the files that term doesn't,
/// e.g. <c>-w:1920</c> also finds files whose resolution is not known.</item>
/// <item><c>s:&lt;10mb</c>, <c>s:&gt;=1gb</c>, <c>s:=0b</c>, <c>s:700mb</c> (approximately) filter by size; units b, kb, mb (default), gb, tb.</item>
/// <item><c>e:mp4</c> filters by extension.</item>
/// <item><c>type:image</c>, <c>type:video</c>, <c>type:audio</c>, <c>type:other</c> filter by kind of file, as the listing
/// classifies extensions; several in a group are alternatives, like <c>e:</c>.</item>
/// <item><c>w:&gt;=1920</c>, <c>h:&lt;720</c> filter by the width or height in pixels of images and videos, <c>r:&gt;720x1024</c> by
/// both (each side compared); <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>, <c>=</c>, none = exactly. Files whose
/// resolution is not known never match.</item>
/// <item><c>r:hr</c>, <c>r:lr</c> find the images and videos of high or low resolution, by the thresholds of the HR/LR
/// marks (shorter side, one threshold for images, one for videos).</item>
/// <item><c>d:&gt;10m</c>, <c>d:&lt;=30s</c>, <c>d:1h</c>, <c>d:&gt;1:30:00</c> filter videos and audio by duration; units s, m
/// (default), h, or a clock (<c>m:ss</c>, <c>h:mm:ss</c>); without an operator, about (rounded to the unit). Files
/// whose duration is not known never match.</item>
/// <item>a leading <c>*</c> matches against the whole path instead of the file name.</item>
/// <item><c>t:name</c> or <c>tag:name</c> finds files with a tag named so (name, shorthand or alias), or with any tag
/// inheriting from it (TagStudio's tag search). <c>special:untagged</c> finds files without tags.</item>
/// <item>double quotes keep spaces and commas in a term: <c>t:"Science Fiction"</c>. In tag names, <c>_</c> also
/// stands for a space.</item>
/// </list>
/// The web client's <c>search-explain.ts</c> (src/Sfe.Web/src/app/library/search-explain) mirrors this parser to
/// put a query into words: change both.
/// </summary>
public sealed record SearchQuery(bool MatchFullPath, IReadOnlyList<SearchGroup> Groups)
{
    public const char Or = ',';
    public const char And = ' ';
    public const char Not = '-';
    public const char Quote = '"';
    public const char FullPathMarker = '*';
    public const string SizePrefix = "s:";
    public const string ExtensionPrefix = "e:";
    public const string WidthPrefix = "w:";
    public const string HeightPrefix = "h:";
    public const string ResolutionPrefix = "r:";
    public const string DurationPrefix = "d:";
    public const string TypePrefix = "type:";
    public static readonly string[] TagPrefixes = ["t:", "tag:"];
    public const string Untagged = "special:untagged";

    public bool IsEmpty => Groups.Count == 0;

    /// <summary>Names of the tags the query refers to.</summary>
    public IEnumerable<string> TagNames => Groups.SelectMany(g => g.Tags).Select(t => t.Name).OfType<string>().Distinct();

    private static readonly Regex SizePattern = new(
        @"^(?<op><=|>=|<|>|=)?(?<num>\d+(?:\.\d+)?)(?<unit>b|kb|mb|gb|tb)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PixelsPattern = new(
        @"^(?<op><=|>=|<|>|=)?(?<num>\d{1,9})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ResolutionPattern = new(
        @"^(?<op><=|>=|<|>|=)?(?<width>\d{1,9})x(?<height>\d{1,9})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DurationPattern = new(
        @"^(?<op><=|>=|<|>|=)?(?:(?<num>\d{1,9}(?:\.\d+)?)(?<unit>s|m|h)?|(?<clock>\d{1,6}(?::\d{1,2}){1,2}))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static SearchQuery Parse(string? query)
    {
        query = (query ?? string.Empty).Trim();
        var matchFullPath = query.StartsWith(FullPathMarker);
        if (matchFullPath)
            query = query.Replace(FullPathMarker.ToString(), string.Empty);

        var groups = SplitOutsideQuotes(query, Or)
            .Select(ParseGroup)
            .Where(g => !g.IsEmpty)
            .ToList();

        return new SearchQuery(matchFullPath, groups);
    }

    /// <summary>Splits on <paramref name="separator"/> except inside double quotes; parts are trimmed, empty ones dropped.</summary>
    internal static IEnumerable<string> SplitOutsideQuotes(string text, char separator)
    {
        var start = 0;
        var quoted = false;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] == Quote)
                quoted = !quoted;
            if (i < text.Length && (quoted || text[i] != separator))
                continue;

            var part = text[start..i].Trim();
            if (part.Length > 0)
                yield return part;
            start = i + 1;
        }
    }

    private static string Unquote(string value) => value.Replace(Quote.ToString(), string.Empty);

    private static SearchGroup ParseGroup(string group)
    {
        var include = new List<string>();
        var exclude = new List<string>();
        var filters = new Filters();
        var tags = new List<TagFilter>();
        var excludedFilters = new List<SearchGroup>();

        foreach (var token in SplitOutsideQuotes(group, And))
        {
            var negated = token[0] == Not;
            var body = negated ? token[1..] : token;
            if (TryParseTag(body, negated, out var tag))
            {
                tags.Add(tag);
            }
            else if (negated && IsFilter(body))
            {
                // NOT of the whole term: "-r:1920x1080" is "not both sides", not "neither side"; several "-e:" exclude each one.
                var excludedFilter = new Filters();
                AddFilter(body, excludedFilter);
                var excluded = excludedFilter.ToGroup([], [], [], []);
                if (!excluded.IsEmpty)
                    excludedFilters.Add(excluded);
            }
            else if (!negated && AddFilter(body, filters))
            {
                // Added.
            }
            else if (Unquote(body) is { Length: > 0 } term)
            {
                (negated ? exclude : include).Add(term);
            }
        }

        return filters.ToGroup(include, exclude, tags, excludedFilters);
    }

    /// <summary>The filter terms of a group (or of one negated term) as they are read.</summary>
    private sealed class Filters
    {
        public List<SizeFilter> Sizes { get; } = [];
        public List<string> Extensions { get; } = [];
        public List<DimensionFilter> Dimensions { get; } = [];
        public List<DurationFilter> Durations { get; } = [];
        public List<ResolutionLevel> Levels { get; } = [];
        public List<ItemKind> Kinds { get; } = [];

        public SearchGroup ToGroup(List<string> include, List<string> exclude, List<TagFilter> tags, List<SearchGroup> excludedFilters) =>
            new(include, exclude, Sizes, Extensions, tags, Dimensions, excludedFilters, Durations, Levels, Kinds);
    }

    private static readonly string[] FilterPrefixes = [SizePrefix, ExtensionPrefix, WidthPrefix, HeightPrefix, ResolutionPrefix, DurationPrefix, TypePrefix];

    private static bool IsFilter(string token) => FilterPrefixes.Any(p => token.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds a size, resolution, duration, extension or type filter; false when the token is none. Malformed ones are ignored.</summary>
    private static bool AddFilter(string token, Filters filters)
    {
        if (!IsFilter(token))
            return false;

        if (token.StartsWith(SizePrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (SizeFilter.TryParse(token[SizePrefix.Length..], out var size))
                filters.Sizes.Add(size);
        }
        else if (token.StartsWith(ExtensionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var extension = token[ExtensionPrefix.Length..].TrimStart('.');
            if (extension.Length > 0)
                filters.Extensions.Add("." + extension);
        }
        else if (token.StartsWith(TypePrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseKind(token[TypePrefix.Length..], out var kind))
                filters.Kinds.Add(kind);
        }
        else if (token.StartsWith(DurationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (DurationFilter.TryParse(token[DurationPrefix.Length..], out var duration))
                filters.Durations.Add(duration);
        }
        else if (token.StartsWith(ResolutionPrefix, StringComparison.OrdinalIgnoreCase) && TryParseLevel(token[ResolutionPrefix.Length..], out var level))
        {
            filters.Levels.Add(level);
        }
        else
        {
            filters.Dimensions.AddRange(DimensionFilter.Parse(token));
        }
        return true;
    }

    /// <summary><c>hr</c> or <c>lr</c> after <c>r:</c>, in any case.</summary>
    private static bool TryParseLevel(string value, out ResolutionLevel level)
    {
        level = value.ToLowerInvariant() switch
        {
            "hr" => ResolutionLevel.High,
            "lr" => ResolutionLevel.Low,
            _ => (ResolutionLevel)(-1),
        };
        return Enum.IsDefined(level);
    }

    /// <summary>The kind after <c>type:</c>, in any case; <c>other</c> is every file that is neither image, video nor audio.</summary>
    private static bool TryParseKind(string value, out ItemKind kind)
    {
        kind = value.ToLowerInvariant() switch
        {
            "image" => ItemKind.Image,
            "video" => ItemKind.Video,
            "audio" => ItemKind.Audio,
            "other" => ItemKind.File,
            _ => ItemKind.Directory,
        };
        return kind != ItemKind.Directory;
    }

    private static bool TryParseTag(string token, bool negated, out TagFilter filter)
    {
        filter = null!;
        if (token.Equals(Untagged, StringComparison.OrdinalIgnoreCase))
        {
            // "untagged" is "not any tag"; "-special:untagged" is "any tag".
            filter = new TagFilter(null, !negated);
            return true;
        }

        var prefix = TagPrefixes.FirstOrDefault(p => token.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        if (prefix is null)
            return false;

        var name = Unquote(token[prefix.Length..]).Trim();
        if (name.Length == 0)
            return false;

        filter = new TagFilter(name, negated);
        return true;
    }

    internal static Match MatchSize(string value) => SizePattern.Match(value);
    internal static Match MatchPixels(string value) => PixelsPattern.Match(value);
    internal static Match MatchResolution(string value) => ResolutionPattern.Match(value);
    internal static Match MatchDuration(string value) => DurationPattern.Match(value);

    // ',' never reaches here as a decimal separator: it separates alternatives.
    internal static double ParseNumber(string value) =>
        double.Parse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
}

/// <param name="ExcludedFilters">
/// One group per negated size, extension, resolution, duration or type term (<c>-s:</c>, <c>-e:</c>, <c>-w:</c>, <c>-h:</c>,
/// <c>-r:</c>, <c>-d:</c>, <c>-type:</c>): files matching it are left out.
/// </param>
/// <param name="Levels">High or low resolution terms (<c>r:hr</c>, <c>r:lr</c>).</param>
/// <param name="Kinds">Kinds of file (<c>type:</c>), alternatives; <see cref="ItemKind.File"/> = none of the media kinds.</param>
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
    IReadOnlyList<ItemKind> Kinds)
{
    public bool IsEmpty => Include.Count == 0 && Exclude.Count == 0 && Sizes.Count == 0 && Extensions.Count == 0 && Tags.Count == 0
        && Dimensions.Count == 0 && ExcludedFilters.Count == 0 && Durations.Count == 0 && Levels.Count == 0 && Kinds.Count == 0;
}

/// <summary>An image or video of high or low resolution, as the HR/LR marks say (<c>r:hr</c>, <c>r:lr</c>).</summary>
public enum ResolutionLevel
{
    High,
    Low,
}

/// <summary>The length of a video or audio file compared to a number of seconds (<c>d:</c>).</summary>
/// <param name="Tolerance">Seconds either side of <paramref name="Seconds"/> for <see cref="SizeComparison.Approximately"/>.</param>
public sealed record DurationFilter(SizeComparison Comparison, double Seconds, double Tolerance = 0)
{
    public static bool TryParse(string value, out DurationFilter filter)
    {
        filter = null!;
        var match = SearchQuery.MatchDuration(value);
        if (!match.Success)
            return false;

        double seconds, unit;
        if (match.Groups["clock"].Success)
        {
            // "1:30" is a minute and a half, "1:30:00" an hour and a half, as players show them.
            seconds = match.Groups["clock"].Value.Split(':').Aggregate(0d, (total, part) => total * 60 + int.Parse(part, CultureInfo.InvariantCulture));
            unit = 1;
        }
        else
        {
            unit = match.Groups["unit"].Value.ToLowerInvariant() switch
            {
                "s" => 1,
                "h" => 3600,
                _ => 60,
            };
            seconds = SearchQuery.ParseNumber(match.Groups["num"].Value) * unit;
        }

        var comparison = match.Groups["op"].Value switch
        {
            "<" => SizeComparison.LessThan,
            "<=" => SizeComparison.LessThanOrEqual,
            ">" => SizeComparison.GreaterThan,
            ">=" => SizeComparison.GreaterThanOrEqual,
            "=" => SizeComparison.Equal,
            _ => SizeComparison.Approximately,
        };

        // "d:10m" is about ten minutes: anything that rounds to it in the unit typed.
        filter = new DurationFilter(comparison, seconds, comparison == SizeComparison.Approximately ? unit / 2 : 0);
        return true;
    }
}

/// <param name="Name">Tag name, shorthand or alias; null means "any tag".</param>
/// <param name="Negated">Files must not have the tag.</param>
public sealed record TagFilter(string? Name, bool Negated = false);

public enum SizeComparison
{
    Approximately,
    Equal,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
}

public sealed record SizeFilter(SizeComparison Comparison, long Bytes, long Tolerance = 0)
{
    private const long Kb = 1024;

    public static bool TryParse(string value, out SizeFilter filter)
    {
        filter = null!;
        var match = SearchQuery.MatchSize(value);
        if (!match.Success)
            return false;

        var unit = match.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "b" => 1L,
            "kb" => Kb,
            "gb" => Kb * Kb * Kb,
            "tb" => Kb * Kb * Kb * Kb,
            _ => Kb * Kb,
        };
        var bytes = (long)Math.Round(SearchQuery.ParseNumber(match.Groups["num"].Value) * unit);
        var comparison = match.Groups["op"].Value switch
        {
            "<" => SizeComparison.LessThan,
            "<=" => SizeComparison.LessThanOrEqual,
            ">" => SizeComparison.GreaterThan,
            ">=" => SizeComparison.GreaterThanOrEqual,
            "=" => SizeComparison.Equal,
            _ => SizeComparison.Approximately,
        };

        // "s:700mb" means "about 700 MB": anything that rounds to it in the requested unit.
        filter = new SizeFilter(comparison, bytes, comparison == SizeComparison.Approximately ? unit / 2 : 0);
        return true;
    }
}

public enum Dimension
{
    Width,
    Height,
}

/// <summary>A side of an image or video compared to a number of pixels (<c>w:</c>, <c>h:</c>, <c>r:</c>).</summary>
public sealed record DimensionFilter(Dimension Dimension, SizeComparison Comparison, int Pixels)
{
    /// <summary>The filters of a <c>w:</c>, <c>h:</c> or <c>r:</c> token: none when malformed, two for <c>r:</c>.</summary>
    public static IReadOnlyList<DimensionFilter> Parse(string token)
    {
        var prefix = token[..2].ToLowerInvariant();
        var value = token[2..];
        if (prefix == SearchQuery.ResolutionPrefix)
        {
            var match = SearchQuery.MatchResolution(value);
            if (!match.Success || !TryPixels(match.Groups["width"].Value, out var width) || !TryPixels(match.Groups["height"].Value, out var height))
                return [];
            var comparison = ToComparison(match.Groups["op"].Value);
            return [new(Dimension.Width, comparison, width), new(Dimension.Height, comparison, height)];
        }

        var side = SearchQuery.MatchPixels(value);
        return side.Success && TryPixels(side.Groups["num"].Value, out var pixels)
            ? [new(prefix == SearchQuery.WidthPrefix ? Dimension.Width : Dimension.Height, ToComparison(side.Groups["op"].Value), pixels)]
            : [];
    }

    private static bool TryPixels(string value, out int pixels) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out pixels);

    /// <summary>Without an operator, a number of pixels is exact: there is no "about" as for sizes.</summary>
    private static SizeComparison ToComparison(string op) => op switch
    {
        "<" => SizeComparison.LessThan,
        "<=" => SizeComparison.LessThanOrEqual,
        ">" => SizeComparison.GreaterThan,
        ">=" => SizeComparison.GreaterThanOrEqual,
        _ => SizeComparison.Equal,
    };
}
