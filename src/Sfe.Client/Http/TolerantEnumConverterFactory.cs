using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Otools.Net.Sfe.Client.Http;

/// <summary>Gives every enum of the library its <see cref="TolerantEnumConverter{T}"/>.</summary>
internal sealed class TolerantEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(TolerantEnumConverter<>).MakeGenericType(typeToConvert))!;
}
