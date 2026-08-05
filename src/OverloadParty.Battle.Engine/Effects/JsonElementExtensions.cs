using System.Text.Json;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// JsonElement のオプショナルプロパティ取得を集約する拡張メソッド。
/// </summary>
internal static class JsonElementExtensions
{
    /// <summary>プロパティが存在するかを返します。</summary>
    /// <param name="element">親 JsonElement。</param>
    /// <param name="propertyName">プロパティ名。</param>
    /// <returns>プロパティが存在する場合 true。</returns>
    public static bool HasProperty(this JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out _);

    /// <summary>プロパティを string で取得します。未指定なら null。</summary>
    /// <param name="element">親 JsonElement。</param>
    /// <param name="propertyName">プロパティ名。</param>
    /// <returns>プロパティ値の string。未指定または明示 null なら null。</returns>
    public static string? GetStringOrNull(this JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var p) ? p.GetString() : null;

    /// <summary>プロパティを string で取得します。未指定なら defaultValue を返します。</summary>
    /// <param name="element">親 JsonElement。</param>
    /// <param name="propertyName">プロパティ名。</param>
    /// <param name="defaultValue">未指定時のフォールバック値。</param>
    /// <returns>プロパティ値の string。未指定または明示 null なら defaultValue。</returns>
    public static string GetStringOr(this JsonElement element, string propertyName, string defaultValue)
        => element.TryGetProperty(propertyName, out var p) ? (p.GetString() ?? defaultValue) : defaultValue;

    /// <summary>プロパティを int で取得します。未指定なら defaultValue を返します。</summary>
    /// <param name="element">親 JsonElement。</param>
    /// <param name="propertyName">プロパティ名。</param>
    /// <param name="defaultValue">未指定時のフォールバック値。</param>
    /// <returns>プロパティ値の int。未指定なら defaultValue。</returns>
    public static int GetInt32Or(this JsonElement element, string propertyName, int defaultValue)
        => element.TryGetProperty(propertyName, out var p) ? p.GetInt32() : defaultValue;

    /// <summary>プロパティを int? で取得します。未指定なら null。</summary>
    /// <param name="element">親 JsonElement。</param>
    /// <param name="propertyName">プロパティ名。</param>
    /// <returns>プロパティ値の int。未指定なら null。</returns>
    public static int? GetInt32OrNull(this JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var p) ? p.GetInt32() : null;

    /// <summary>プロパティを long で取得します。未指定なら defaultValue を返します。</summary>
    /// <param name="element">親 JsonElement。</param>
    /// <param name="propertyName">プロパティ名。</param>
    /// <param name="defaultValue">未指定時のフォールバック値。</param>
    /// <returns>プロパティ値の long。未指定なら defaultValue。</returns>
    public static long GetInt64Or(this JsonElement element, string propertyName, long defaultValue)
        => element.TryGetProperty(propertyName, out var p) ? p.GetInt64() : defaultValue;

    /// <summary>プロパティを long? で取得します。未指定なら null。</summary>
    /// <param name="element">親 JsonElement。</param>
    /// <param name="propertyName">プロパティ名。</param>
    /// <returns>プロパティ値の long。未指定なら null。</returns>
    public static long? GetInt64OrNull(this JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var p) ? p.GetInt64() : null;

    /// <summary>プロパティを bool フラグで取得します。未指定または非 true なら false。</summary>
    /// <param name="element">親 JsonElement。</param>
    /// <param name="propertyName">プロパティ名。</param>
    /// <returns>プロパティ値が true なら true、それ以外 (未指定含む) は false。</returns>
    public static bool GetBoolOrFalse(this JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var p)
            && p.ValueKind == JsonValueKind.True;

    /// <summary>プロパティを string リストとして取得します。単一値・配列のどちらの書き方も受けます。</summary>
    /// <param name="element">親 JsonElement。</param>
    /// <param name="propertyName">プロパティ名。</param>
    /// <returns>値の string リスト。未指定または明示 null なら null。</returns>
    public static List<string>? GetStringListOrNull(this JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var p) ? ReadStringList(p, propertyName) : null;

    // ─── Dictionary<string, JsonElement> 向け (custom 効果の meta) ──────────

    /// <summary>meta dictionary からキーを string で取得します。未指定なら null。</summary>
    /// <param name="meta">meta dictionary。</param>
    /// <param name="key">キー名。</param>
    /// <returns>値の string。未指定または明示 null なら null。</returns>
    public static string? GetStringOrNull(this Dictionary<string, JsonElement> meta, string key)
        => meta.TryGetValue(key, out var p) ? p.GetString() : null;

    /// <summary>meta dictionary からキーを long で取得します。未指定なら defaultValue を返します。</summary>
    /// <param name="meta">meta dictionary。</param>
    /// <param name="key">キー名。</param>
    /// <param name="defaultValue">未指定時のフォールバック値。</param>
    /// <returns>値の long。未指定なら defaultValue。</returns>
    public static long GetInt64Or(this Dictionary<string, JsonElement> meta, string key, long defaultValue)
        => meta.TryGetValue(key, out var p) ? p.GetInt64() : defaultValue;

    /// <summary>meta dictionary からキーを int で取得します。未指定なら defaultValue を返します。</summary>
    /// <param name="meta">meta dictionary。</param>
    /// <param name="key">キー名。</param>
    /// <param name="defaultValue">未指定時のフォールバック値。</param>
    /// <returns>値の int。未指定なら defaultValue。</returns>
    public static int GetInt32Or(this Dictionary<string, JsonElement> meta, string key, int defaultValue)
        => meta.TryGetValue(key, out var p) ? p.GetInt32() : defaultValue;

    /// <summary>meta dictionary からキーを string リストで取得します。単一値・配列のどちらの書き方も受けます。</summary>
    /// <param name="meta">meta dictionary。</param>
    /// <param name="key">キー名。</param>
    /// <returns>値の string リスト。未指定または明示 null なら null。</returns>
    public static List<string>? GetStringListOrNull(this Dictionary<string, JsonElement> meta, string key)
        => meta.TryGetValue(key, out var p) ? ReadStringList(p, key) : null;

    /// <summary>単一値・配列のどちらでも書ける値を string リストとして読みます。</summary>
    /// <param name="value">読み取る値。</param>
    /// <param name="name">エラーメッセージに出すキー名。</param>
    /// <returns>値の string リスト。明示 null なら null。</returns>
    private static List<string>? ReadStringList(JsonElement value, string name) => value.ValueKind switch
    {
        JsonValueKind.String => [value.GetString()!],
        JsonValueKind.Array => value.EnumerateArray().Select(e => e.GetString()!).ToList(),
        JsonValueKind.Null => null,
        _ => throw new InvalidOperationException(
            $"{name} must be a string or an array of strings but was {value.ValueKind}"),
    };
}
