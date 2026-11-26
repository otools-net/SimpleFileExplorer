using System.Text.Json;
using System.Text.Json.Serialization;

namespace Otools.Net.Sfe.Tests.Infrastructure;

/// <summary>Reads the events of a server-sent event stream (the change feed).</summary>
public static class ServerEvents
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// The name and data of the next event, whatever it is; fails after 10 s or when the stream ends.
    /// </summary>
    public static async Task<(string Name, string Data)> NextAsync(StreamReader reader, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        string? current = null;
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
                current = line["event: ".Length..];
            else if (line.StartsWith("data: ", StringComparison.Ordinal) && current is not null)
                return (current, line["data: ".Length..]);
        }
        throw new AssertFailedException("The feed ended.");
    }

    /// <summary>The data of the next event named <paramref name="name"/>, the others skipped.</summary>
    public static async Task<T> NextAsync<T>(StreamReader reader, string name, CancellationToken token)
    {
        while (true)
        {
            var (current, data) = await NextAsync(reader, token);
            if (current == name)
                return JsonSerializer.Deserialize<T>(data, Json)!;
        }
    }

    /// <summary>The events up to the first named <paramref name="name"/> (left out), by name.</summary>
    public static async Task<List<string>> NamesBeforeAsync(StreamReader reader, string name, CancellationToken token)
    {
        var names = new List<string>();
        while ((await NextAsync(reader, token)).Name is var current && current != name)
            names.Add(current);
        return names;
    }
}
