namespace OverloadParty.Battle.Models;

/// <summary>
/// Initiative はプロダクトに属する施策 1 件分を保持します。Effect は効果 DSL (ops / custom) で、
/// 起動効果と同じパイプラインで実行します。
/// </summary>
public class Initiative : IEffectSource
{
    // 施策効果はカード ID と衝突しない合成キーで EffectRegistry に登録する。
    private const string EffectSourceIdPrefix = "initiative:";

    public string InitiativeId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public long InsightCost { get; set; }
    public string EffectText { get; set; } = "";
    public EffectDef Effect { get; set; } = new();

    /// <summary>EffectRegistry への登録キーを合成キーとして返します。</summary>
    public string EffectSourceId => EffectSourceIdPrefix + InitiativeId;

    /// <summary>登録対象の効果定義群を返します。</summary>
    public IReadOnlyList<EffectDef> EffectDefs => [Effect];
}

/// <summary>
/// InitiativeKinds は施策の区分のワイヤー定数を保持します。
/// </summary>
public static class InitiativeKinds
{
    public const string Routine = "routine";
    public const string Special = "special";
}

/// <summary>
/// UseInitiativeEventData は施策使用イベントのペイロードを保持します。
/// </summary>
public class UseInitiativeEventData : IEventData
{
    [System.Text.Json.Serialization.JsonPropertyName("productId")]
    public string ProductId { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("initiativeId")]
    public string InitiativeId { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("initiativeName")]
    public string InitiativeName { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("insightCost")]
    public long InsightCost { get; set; }
}
