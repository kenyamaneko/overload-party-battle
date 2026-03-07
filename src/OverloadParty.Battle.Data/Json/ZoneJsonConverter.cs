using System.Text.Json;
using System.Text.Json.Serialization;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Data.Json;

/// <summary>
/// Serializes/deserializes Zone&lt;T&gt; as a fixed-length nullable array
/// to preserve slot positions in the wire format: [null, {...}, null].
/// </summary>
public class ZoneJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType &&
        typeToConvert.GetGenericTypeDefinition() == typeof(Zone<>);

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var elementType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(ZoneJsonConverter<>).MakeGenericType(elementType);
        return (JsonConverter?)Activator.CreateInstance(converterType);
    }
}

public class ZoneJsonConverter<T> : JsonConverter<Zone<T>> where T : class
{
    public override Zone<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var array = JsonSerializer.Deserialize<T?[]>(ref reader, options);
        if (array is null)
        {
            return null;
        }

        var zone = new Zone<T>(array.Length);
        for (int i = 0; i < array.Length; i++)
        {
            zone[i] = array[i];
        }
        return zone;
    }

    public override void Write(Utf8JsonWriter writer, Zone<T> value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value.ToArray(), options);
    }
}
