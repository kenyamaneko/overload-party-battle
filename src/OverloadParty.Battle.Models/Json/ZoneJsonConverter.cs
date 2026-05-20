using System.Text.Json;
using System.Text.Json.Serialization;

namespace OverloadParty.Battle.Models.Json;

/// <summary>
/// Serializes/deserializes Zone&lt;T&gt; as a fixed-length nullable array
/// to preserve slot positions in the wire format: [null, {...}, null].
/// </summary>
public class ZoneJsonConverterFactory : JsonConverterFactory
{
    /// <summary>
    /// Zone&lt;T&gt; 型に対する変換可否を返します
    /// </summary>
    /// <param name="typeToConvert">判定対象の型</param>
    /// <returns>Zone&lt;T&gt; を変換可能な場合 true</returns>
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType &&
        typeToConvert.GetGenericTypeDefinition() == typeof(Zone<>);

    /// <summary>
    /// Zone&lt;T&gt; 用の JsonConverter インスタンスを生成します
    /// </summary>
    /// <param name="typeToConvert">変換対象の型</param>
    /// <param name="options">シリアライザオプション</param>
    /// <returns>生成された JsonConverter</returns>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var elementType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(ZoneJsonConverter<>).MakeGenericType(elementType);
        return (JsonConverter?)Activator.CreateInstance(converterType);
    }
}

public class ZoneJsonConverter<T> : JsonConverter<Zone<T>> where T : class
{
    /// <summary>
    /// JSON 配列から Zone&lt;T&gt; をデシリアライズします
    /// </summary>
    /// <param name="reader">JSON リーダー</param>
    /// <param name="typeToConvert">変換対象の型</param>
    /// <param name="options">シリアライザオプション</param>
    /// <returns>復元された Zone&lt;T&gt;。入力が null なら null</returns>
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

    /// <summary>
    /// Zone&lt;T&gt; を JSON 配列にシリアライズします
    /// </summary>
    /// <param name="writer">JSON ライター</param>
    /// <param name="value">シリアライズ対象の Zone&lt;T&gt;</param>
    /// <param name="options">シリアライザオプション</param>
    public override void Write(Utf8JsonWriter writer, Zone<T> value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value.ToArray(), options);
    }
}
