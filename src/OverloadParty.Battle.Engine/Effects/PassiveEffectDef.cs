namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// パッシブ効果 1 件がフィールドに適用するバフ 1 本を表します。
/// </summary>
public sealed class PassiveBuffApplication
{
    /// <summary>バフの適用対象を選ぶセレクタ。</summary>
    public required ISelector Selector { get; init; }

    /// <summary>適用するバフの種別 (MapBuffName 適用後の内部表現)。</summary>
    public required string EffectType { get; init; }

    /// <summary>バフの数値を解決するリゾルバ。</summary>
    public required IAmountResolver Amount { get; init; }

    /// <summary>バフの適用方式 (flat / percent)。</summary>
    public required string Mode { get; init; }
}

/// <summary>
/// パッシブ効果 (フィールド状態から常時導出される継続効果) の発動条件とバフ適用内容。
/// </summary>
public sealed class PassiveEffectDef
{
    /// <summary>成立を要する発動条件。全て満たされたときのみ Applications を適用する。</summary>
    public required IEffectGuard[] Guards { get; init; }

    /// <summary>発動条件成立時に適用するバフ群。</summary>
    public required PassiveBuffApplication[] Applications { get; init; }
}
