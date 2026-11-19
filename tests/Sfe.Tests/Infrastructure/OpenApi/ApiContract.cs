using System.Text.Json;
using Otools.Net.Sfe.Api;

namespace Otools.Net.Sfe.Tests.Infrastructure.OpenApi;

/// <summary>
/// The promises of an OpenAPI document, one <see cref="ApiFact"/> per key: <c>GET /api/v1/tags</c>, its
/// <c>GET /api/v1/tags · parameter query:path</c>, <c>· body</c>, <c>· status 200</c>, <c>· rights</c>; then
/// <c>schema Tag</c>, <c>schema Tag · property name</c>, <c>schema Tag · enum</c>; <c>problem types</c>. Texts
/// (summaries, descriptions) and the version are left out: they change without breaking anything.
/// </summary>
public static class ApiContract
{
    public const string Separator = " · ";
    public const string ProblemTypesKey = "problem types";

    private static readonly string[] Methods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    public static SortedDictionary<string, ApiFact> Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Read(document.RootElement);
    }

    public static SortedDictionary<string, ApiFact> Read(JsonElement document)
    {
        var facts = new SortedDictionary<string, ApiFact>(StringComparer.Ordinal);
        foreach (var (operation, key) in Operations(document))
            AddOperation(facts, key, operation);
        AddSchemas(facts, document);
        if (document.TryGetProperty(ApiDocument.ProblemTypesExtension, out var words))
            facts[ProblemTypesKey] = new ApiFact(FactKind.ProblemTypes, Words: Sorted(words));
        return facts;
    }

    /// <summary>The key a fact belongs to (its route or schema), or the fact's own key.</summary>
    public static string OwnerOf(string key) =>
        key.IndexOf(Separator, StringComparison.Ordinal) is var at and >= 0 ? key[..at] : key;

    /// <summary>Every operation with its key, <c>GET /api/v1/tags</c>.</summary>
    public static IEnumerable<(JsonElement Operation, string Key)> Operations(JsonElement document)
    {
        if (!document.TryGetProperty("paths", out var paths))
            yield break;
        foreach (var path in paths.EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject().Where(m => Methods.Contains(m.Name)))
                yield return (method.Value, $"{method.Name.ToUpperInvariant()} {path.Name}");
        }
    }

    private static void AddOperation(SortedDictionary<string, ApiFact> facts, string key, JsonElement operation)
    {
        facts[key] = new ApiFact(FactKind.Route);
        if (operation.TryGetProperty("parameters", out var parameters))
        {
            foreach (var parameter in parameters.EnumerateArray())
                AddParameter(facts, key, parameter);
        }
        if (operation.TryGetProperty("requestBody", out var body))
            AddBody(facts, key, body);
        if (operation.TryGetProperty("responses", out var responses))
        {
            foreach (var status in responses.EnumerateObject())
                AddStatus(facts, key, status);
        }
        if (operation.TryGetProperty(ApiDocument.RightsExtension, out var rights))
        {
            var requirements = rights.EnumerateArray().Select(anyOf => string.Join("|", Sorted(anyOf)));
            facts[key + Separator + "rights"] = new ApiFact(FactKind.Rights, Words: [.. requirements.Order()]);
        }
    }

    private static void AddParameter(SortedDictionary<string, ApiFact> facts, string key, JsonElement parameter)
    {
        var name = $"{parameter.GetProperty("in").GetString()}:{parameter.GetProperty("name").GetString()}";
        var schema = parameter.TryGetProperty("schema", out var found) ? found : default;
        facts[key + Separator + "parameter " + name] = new ApiFact(
            FactKind.Parameter,
            Required: IsTrue(parameter, "required"),
            Types: schema.ValueKind == JsonValueKind.Object ? ApiSchemas.TypesOf(schema) : [],
            Default: DefaultOf(schema),
            Flow: Flow.Input
        );
    }

    private static void AddBody(SortedDictionary<string, ApiFact> facts, string key, JsonElement body)
    {
        facts[key + Separator + "body"] = new ApiFact(
            FactKind.Body,
            Required: IsTrue(body, "required"),
            Flow: Flow.Input
        );
        foreach (var (media, types) in Contents(body))
        {
            facts[key + Separator + "body " + media] = new ApiFact(
                FactKind.BodyContent,
                Types: types,
                Flow: Flow.Input
            );
        }
    }

    private static void AddStatus(SortedDictionary<string, ApiFact> facts, string key, JsonProperty status)
    {
        var statusKey = key + Separator + "status " + status.Name;
        facts[statusKey] = new ApiFact(FactKind.Status, Flow: Flow.Output);
        foreach (var (media, types) in Contents(status.Value))
            facts[statusKey + " " + media] = new ApiFact(FactKind.StatusContent, Types: types, Flow: Flow.Output);
    }

    private static IEnumerable<(string Media, IReadOnlyList<string> Types)> Contents(JsonElement element)
    {
        if (!element.TryGetProperty("content", out var content))
            yield break;
        foreach (var media in content.EnumerateObject())
        {
            var types = media.Value.TryGetProperty("schema", out var schema) ? ApiSchemas.TypesOf(schema) : [];
            yield return (media.Name, types);
        }
    }

    private static void AddSchemas(SortedDictionary<string, ApiFact> facts, JsonElement document)
    {
        if (!document.TryGetProperty("components", out var components))
            return;
        if (!components.TryGetProperty("schemas", out var schemas))
            return;
        var flows = ApiSchemas.FlowsOf(document);
        foreach (var schema in schemas.EnumerateObject())
            AddSchema(facts, schema, flows.GetValueOrDefault(schema.Name, Flow.Both));
    }

    private static void AddSchema(SortedDictionary<string, ApiFact> facts, JsonProperty schema, Flow flow)
    {
        var key = "schema " + schema.Name;
        var value = schema.Value;
        facts[key] = new ApiFact(
            FactKind.Schema,
            Types: ApiSchemas.TypesOf(value),
            Default: DefaultOf(value),
            Flow: flow
        );
        if (value.TryGetProperty("enum", out var words))
            facts[key + Separator + "enum"] = new ApiFact(FactKind.Enum, Words: Sorted(words), Flow: flow);
        if (!value.TryGetProperty("properties", out var properties))
            return;
        var required = value.TryGetProperty("required", out var names) ? Sorted(names) : [];
        foreach (var property in properties.EnumerateObject())
        {
            facts[key + Separator + "property " + property.Name] = new ApiFact(
                FactKind.Property,
                Required: required.Contains(property.Name),
                Types: ApiSchemas.TypesOf(property.Value),
                Default: DefaultOf(property.Value),
                Flow: flow
            );
        }
    }

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? DefaultOf(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("default", out var value)
            ? value.GetRawText()
            : null;

    private static IReadOnlyList<string> Sorted(JsonElement words) =>
        words.EnumerateArray().Select(word => word.ToString()).Order(StringComparer.Ordinal).ToList();
}
