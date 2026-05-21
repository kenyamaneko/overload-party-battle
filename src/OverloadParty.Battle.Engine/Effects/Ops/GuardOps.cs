using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// guard が検査するリソースまたはカードの指定先。
/// </summary>
public enum MatchSelector
{
    /// <summary>効果の対象リソース。</summary>
    Target,

    /// <summary>トリガー event を発生させたカード (インシデントカード等)。</summary>
    EventCard,

    /// <summary>on_attack_declared で攻撃を宣言したリソース。</summary>
    Attacker,
}

/// <summary>
/// 参照比較 guard (same / not_same) が指すリソースの指定先。
/// </summary>
public enum ResourceRef
{
    /// <summary>効果のソースリソース。</summary>
    Source,

    /// <summary>効果の対象リソース。</summary>
    Target,

    /// <summary>Attachment ソースの装備先リソース。</summary>
    EquipHost,
}

/// <summary>
/// パイプライン context 内で <see cref="ResourceRef"/> のインスタンス ID を解決します。
/// </summary>
internal static class ResourceRefResolver
{
    /// <summary>指定参照のインスタンス ID を解決します。解決できないときは例外を投げます。</summary>
    /// <param name="refKind">解決する参照の種別。</param>
    /// <param name="ctx">パイプライン実行コンテキスト。</param>
    /// <returns>解決したインスタンス ID。</returns>
    public static string Resolve(ResourceRef refKind, OpContext ctx) => refKind switch
    {
        ResourceRef.Source => (ctx.Source
            ?? throw new GameRuleException("No source for reference guard")).InstanceID,
        ResourceRef.Target => (ctx.Target
            ?? throw new GameRuleException("No target for reference guard")).InstanceID,
        ResourceRef.EquipHost => ctx.SupSource?.TargetInstanceID
            ?? throw new GameRuleException("Source is not an attachment with an equip host"),
        _ => throw new GameRuleException($"Unsupported resource reference: {refKind}"),
    };

    /// <summary>指定参照のインスタンス ID を解決します。解決できないときは null を返します (述語向け)。</summary>
    /// <param name="refKind">解決する参照の種別。</param>
    /// <param name="ctx">効果実行コンテキスト。</param>
    /// <returns>解決したインスタンス ID。解決できない場合は null。</returns>
    public static string? TryResolve(ResourceRef refKind, EffectContext ctx) => refKind switch
    {
        ResourceRef.Source => ctx.Source?.InstanceID,
        ResourceRef.Target => ctx.Target?.InstanceID,
        ResourceRef.EquipHost => ctx.SupSource?.TargetInstanceID,
        _ => null,
    };
}
