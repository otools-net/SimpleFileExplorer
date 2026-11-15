using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Otools.Net.Sfe.Client.Http;

/// <summary>
/// Reads an enum from the API's camelCase word, and a word this version doesn't know (a newer server's) as the
/// enum's value 0, <c>Unknown</c>: the v1 rule that a client never fails on what a newer server adds.
/// </summary>
internal sealed class TolerantEnumConverter<T> : JsonConverter<T>
    where T : struct, Enum
{
    private readonly Dictionary<string, T> _byWord = new(StringComparer.OrdinalIgnoreCase);

    public TolerantEnumConverter()
    {
        foreach (T value in Enum.GetValues(typeof(T)))
            _byWord[value.ToString()] = value;
    }

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            return default;
        return _byWord.TryGetValue(reader.GetString() ?? "", out var value) ? value : default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var name = value.ToString();
        writer.WriteStringValue(char.ToLowerInvariant(name[0]) + name.Substring(1));
    }
}
