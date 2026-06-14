namespace OverloadParty.Battle.Models;

/// <summary>
/// Product は陣営に紐づくプロダクト定義を保持します。デッキが選んだプロダクトが、
/// セットできる施策 (ルーチン / スペシャル) を規定します。
/// </summary>
public class Product
{
    public string ProductId { get; set; } = "";
    public string Faction { get; set; } = "";
    public string ProductName { get; set; } = "";
    public List<Initiative> Initiatives { get; set; } = [];

    /// <summary>指定 ID・区分の施策を返します。見つからなければ null を返します。</summary>
    /// <param name="initiativeId">施策 ID。</param>
    /// <param name="kind">施策の区分 (ルーチン / スペシャル)。</param>
    /// <returns>該当施策。なければ null。</returns>
    public Initiative? FindInitiative(string initiativeId, string kind) =>
        Initiatives.FirstOrDefault(i => i.InitiativeId == initiativeId && i.Kind == kind);
}

/// <summary>
/// Initiative はプロダクトの施策 1 件分を保持します。Effect は効果 DSL (ops / custom) で、
/// 起動効果と同じパイプラインで実行します。
/// </summary>
public class Initiative
{
    public string InitiativeId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public long InsightCost { get; set; }
    public string EffectText { get; set; } = "";
    public EffectDef Effect { get; set; } = new();
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
/// client 向けの wire 型は未提供のため、現状はエンジン内部・永続化用です。
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
