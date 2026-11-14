using System.Text.Json;

namespace Otools.Net.Sfe.Tests.Infrastructure.OpenApi;

/// <summary>
/// Reads the schemas of an OpenAPI document: the types a value may have, written so that two documents compare
/// (<see cref="TypesOf"/>), and which schemas clients send or receive (<see cref="FlowsOf"/>).
/// </summary>
public static class ApiSchemas
{
    private const string SchemaPrefix = "#/components/schemas/";

    /// <summary>
    /// The alternatives of a value, sorted: <c>string</c>, <c>integer:int64</c>, <c>null</c>, <c>ref:Tag</c>,
    /// <c>array&lt;ref:Tag&gt;</c>, <c>map&lt;string&gt;</c>, <c>enum</c> (an enum schema without a type).
    /// </summary>
    public static IReadOnlyList<string> TypesOf(JsonElement schema) =>
        Alternatives(schema).Distinct().Order(StringComparer.Ordinal).ToList();

    /// <summary>
    /// The schemas reached from what clients send (bodies, parameters) and from what they receive (answers),
    /// following the references between schemas.
    /// </summary>
    public static IReadOnlyDictionary<string, Flow> FlowsOf(JsonElement document)
    {
        var flows = new Dictionary<string, Flow>(StringComparer.Ordinal);
        var schemas =
            document.TryGetProperty("components", out var components)
            && components.TryGetProperty("schemas", out var found)
                ? found
                : default;
        foreach (var (operation, _) in ApiContract.Operations(document))
        {
            if (operation.TryGetProperty("parameters", out var parameters))
                Reach(parameters, Flow.Input, schemas, flows);
            if (operation.TryGetProperty("requestBody", out var body))
                Reach(body, Flow.Input, schemas, flows);
            if (operation.TryGetProperty("responses", out var responses))
                Reach(responses, Flow.Output, schemas, flows);
        }
        return flows;
    }

    private static IEnumerable<string> Alternatives(JsonElement schema)
    {
        if (schema.TryGetProperty("$ref", out var reference))
            yield return "ref:" + NameOf(reference.GetString()!);
        foreach (var keyword in new[] { "oneOf", "anyOf", "allOf" })
        {
            if (!schema.TryGetProperty(keyword, out var members))
                continue;
            foreach (var alternative in members.EnumerateArray().SelectMany(Alternatives))
                yield return alternative;
        }
        if (schema.TryGetProperty("type", out var type))
        {
            var types =
                type.ValueKind == JsonValueKind.Array
                    ? type.EnumerateArray().Select(t => t.GetString()!)
                    : [type.GetString()!];
            foreach (var name in types)
                yield return Alternative(name, schema);
        }
        else if (schema.TryGetProperty("enum", out _))
        {
            yield return "enum";
        }
    }

    private static string Alternative(string type, JsonElement schema)
    {
        if (type == "array" && schema.TryGetProperty("items", out var items))
            return $"array<{string.Join("|", TypesOf(items))}>";
        if (type == "object" && schema.TryGetProperty("additionalProperties", out var values))
            return values.ValueKind == JsonValueKind.Object ? $"map<{string.Join("|", TypesOf(values))}>" : "object";
        if (type == "null" || !schema.TryGetProperty("format", out var format))
            return type;
        return $"{type}:{format.GetString()}";
    }

    /// <summary>Marks the schemas <paramref name="element"/> refers to, and theirs, with the flow.</summary>
    private static void Reach(JsonElement element, Flow flow, JsonElement schemas, Dictionary<string, Flow> flows)
    {
        var waiting = new Stack<string>(ReferencesIn(element));
        while (waiting.TryPop(out var name))
        {
            var known = flows.GetValueOrDefault(name);
            if ((known & flow) == flow)
                continue;
            flows[name] = known | flow;
            if (schemas.ValueKind == JsonValueKind.Object && schemas.TryGetProperty(name, out var schema))
            {
                foreach (var reference in ReferencesIn(schema))
                    waiting.Push(reference);
            }
        }
    }

    private static IEnumerable<string> ReferencesIn(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().SelectMany(ReferencesOf),
            JsonValueKind.Array => element.EnumerateArray().SelectMany(ReferencesIn),
            _ => [],
        };

    private static IEnumerable<string> ReferencesOf(JsonProperty property) =>
        property is { Name: "$ref", Value.ValueKind: JsonValueKind.String }
            ? [NameOf(property.Value.GetString()!)]
            : ReferencesIn(property.Value);

    private static string NameOf(string reference) =>
        reference.StartsWith(SchemaPrefix, StringComparison.Ordinal) ? reference[SchemaPrefix.Length..] : reference;
}
