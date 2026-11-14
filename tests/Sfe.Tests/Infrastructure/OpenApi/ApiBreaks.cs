namespace Otools.Net.Sfe.Tests.Infrastructure.OpenApi;

/// <summary>
/// What a new document breaks of its baseline, by the rules of agents/docs/development/api-versioning.md: a route,
/// parameter, property, enum word, status or problem type word removed or renamed; a new required input; a type
/// that accepts less (what clients send) or gives more (what they receive); a default changed; a right needed where
/// none was. Adding routes, optional inputs, properties, statuses and words breaks nothing.
/// </summary>
public static class ApiBreaks
{
    public static IReadOnlyList<string> Find(
        IReadOnlyDictionary<string, ApiFact> baseline,
        IReadOnlyDictionary<string, ApiFact> current
    )
    {
        var breaks = new List<string>();
        foreach (var (key, before) in baseline)
        {
            if (IsInsideRemoved(key, current))
                continue;
            if (current.TryGetValue(key, out var after))
                breaks.AddRange(Compare(before, after).Select(problem => $"{key}: {problem}"));
            else if (Removal(before) is { } problem)
                breaks.Add($"{key}: {problem}");
        }
        foreach (var (key, after) in current)
        {
            if (!baseline.ContainsKey(key) && baseline.ContainsKey(ApiContract.OwnerOf(key)))
                breaks.AddRange(Addition(after).Select(problem => $"{key}: {problem}"));
        }
        return breaks;
    }

    /// <summary>The keys added, removed or changed: what an update of the baseline would change.</summary>
    public static IReadOnlyList<string> Differences(
        IReadOnlyDictionary<string, ApiFact> baseline,
        IReadOnlyDictionary<string, ApiFact> current
    ) =>
        current
            .Where(fact => !baseline.TryGetValue(fact.Key, out var before) || before != fact.Value)
            .Select(fact => $"{fact.Key}: {fact.Value.Text}")
            .Concat(baseline.Keys.Where(key => !current.ContainsKey(key)).Select(key => $"{key}: removed"))
            .ToList();

    /// <summary>A fact of a route or schema that went is told once, with its owner.</summary>
    private static bool IsInsideRemoved(string key, IReadOnlyDictionary<string, ApiFact> current) =>
        ApiContract.OwnerOf(key) is var owner && owner != key && !current.ContainsKey(owner);

    private static string? Removal(ApiFact before) =>
        before.Kind switch
        {
            FactKind.Route => "route removed or renamed",
            FactKind.Parameter => "parameter removed or renamed",
            FactKind.Body => "the request body is no longer read",
            FactKind.BodyContent => "this content type is no longer accepted",
            FactKind.Status => "status no longer answered",
            FactKind.StatusContent => "this content type is no longer answered",
            FactKind.Schema => "schema removed or renamed",
            FactKind.Property => "property removed or renamed",
            FactKind.Enum => "no longer an enum",
            FactKind.ProblemTypes => "the problem type words are gone",
            // Fewer rights needed: more callers pass, nobody is refused who passed before.
            FactKind.Rights => null,
            _ => $"{before.Kind} removed",
        };

    private static IEnumerable<string> Addition(ApiFact after)
    {
        if (after.Required && after.Kind is FactKind.Parameter or FactKind.Body)
            yield return "new required input";
        if (after is { Kind: FactKind.Property, Required: true } && after.Flow.HasFlag(Flow.Input))
            yield return "new required property in what clients send";
        if (after.Kind == FactKind.Rights)
            yield return $"a right is needed where none was ({string.Join(" and ", after.Words!)})";
    }

    private static IEnumerable<string> Compare(ApiFact before, ApiFact after)
    {
        var flow = before.Flow | after.Flow;
        if (!before.Required && after.Required && flow.HasFlag(Flow.Input))
            yield return "now required in what clients send";
        if (before.Required && !after.Required && after.Kind == FactKind.Property && flow.HasFlag(Flow.Output))
            yield return "may now be missing from answers";
        if (TypeBreak(before.Types, after.Types, flow) is { } types)
            yield return types;
        if (before.Default is not null && before.Default != after.Default)
            yield return $"default changed from {before.Default} to {after.Default ?? "none"}";
        foreach (var problem in WordBreaks(before, after))
            yield return problem;
    }

    private static string? TypeBreak(IReadOnlyList<string>? before, IReadOnlyList<string>? after, Flow flow)
    {
        if (before is null || after is null)
            return null;
        var acceptsLess = before.Except(after).Any();
        var givesMore = after.Except(before).Any();
        var broken = (flow.HasFlag(Flow.Input) && acceptsLess) || (flow.HasFlag(Flow.Output) && givesMore);
        return broken ? $"type changed from {string.Join("|", before)} to {string.Join("|", after)}" : null;
    }

    private static IEnumerable<string> WordBreaks(ApiFact before, ApiFact after)
    {
        if (before.Words is null || after.Words is null)
            yield break;
        if (before.Kind == FactKind.Rights)
        {
            var earlier = string.Join(" and ", before.Words);
            foreach (var requirement in after.Words.Where(r => !IsNoStricter(r, before.Words)))
                yield return $"a right is needed where none was ({requirement}; before: {earlier})";
            yield break;
        }
        foreach (var word in before.Words.Except(after.Words))
            yield return $"word {word} removed or renamed";
    }

    /// <summary>
    /// A requirement (any one of its rights) refuses nobody who passed before when some requirement of before had
    /// only rights among it.
    /// </summary>
    private static bool IsNoStricter(string requirement, IReadOnlyList<string> before)
    {
        var anyOf = requirement.Split('|');
        return before.Any(earlier => earlier.Split('|').All(anyOf.Contains));
    }
}
