using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Otools.Net.Sfe.Client.Http;

/// <summary>
/// The API's JSON: camelCase names, enums as words (unknown ones read as <c>Unknown</c>), numbers also read from
/// strings (the v1 document allows both), unknown properties ignored.
/// </summary>
internal static class SfeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new TolerantEnumConverterFactory() },
    };

    /// <summary>A request body of <paramref name="value"/>.</summary>
    public static HttpContent Content(object value)
    {
        var json = JsonSerializer.Serialize(value, value.GetType(), Options);
        var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return content;
    }

    /// <summary>The answer's body read as <typeparamref name="T"/>; an empty body reads as an error.</summary>
    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken).ConfigureAwait(false);
        return value ?? throw new InvalidDataException("The server answered no content where some was expected.");
    }
}
