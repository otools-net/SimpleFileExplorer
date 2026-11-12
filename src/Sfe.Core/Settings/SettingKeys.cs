using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sfe.Core.Settings;

/// <summary>Settings as JSON, flattened to their keys (<c>browse.defaultPageSize</c>) and back.</summary>
public static class SettingKeys
{
    public static JsonObject ToJson(EditableSettings settings) =>
        JsonSerializer.SerializeToNode(settings, SettingsStore.Json)!.AsObject();

    public static EditableSettings FromJson(JsonObject json) => json.Deserialize<EditableSettings>(SettingsStore.Json)!;

    /// <summary>Objects open into dotted keys; anything else (numbers, text, lists) is a value.</summary>
    public static Dictionary<string, JsonNode?> Flatten(JsonObject json)
    {
        var values = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        Add(json, "");
        return values;

        void Add(JsonObject node, string prefix)
        {
            foreach (var (name, value) in node)
            {
                if (value is JsonObject child)
                    Add(child, prefix + name + ".");
                else
                    values[prefix + name] = value;
            }
        }
    }

    /// <summary>Sets the value of a dotted key, creating the objects on its way.</summary>
    public static void Set(JsonObject json, string key, JsonNode? value)
    {
        var parts = key.Split('.');
        var node = json;
        foreach (var part in parts[..^1])
        {
            if (node[part] is not JsonObject child)
                node[part] = child = new JsonObject();
            node = child;
        }

        node[parts[^1]] = value?.DeepClone();
    }

    public static bool Same(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);
}
