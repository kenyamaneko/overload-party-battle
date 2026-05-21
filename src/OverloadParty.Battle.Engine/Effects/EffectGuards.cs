using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// バジェットが最低値以上か検査する述語。
/// </summary>
public class MinBudgetGuard(long min) : IEffectGuard
{
    /// <summary>必要な最低バジェット。</summary>
    public long Min => min;

    /// <inheritdoc />
    public bool Check(EffectContext ctx) => ctx.State.GetBudget(ctx.PlayerNum) >= min;
}

/// <summary>
/// バジェットが最大値以下か検査する述語。
/// </summary>
public class MaxBudgetGuard(long max) : IEffectGuard
{
    /// <summary>許容される最大バジェット。</summary>
    public long Max => max;

    /// <inheritdoc />
    public bool Check(EffectContext ctx) => ctx.State.GetBudget(ctx.PlayerNum) <= max;
}

/// <summary>
/// セレクタが指すリソース / カードが faction / card type / card id / owner 条件に一致するか検査する述語。
/// </summary>
public class MatchGuard(
    MatchSelector selector,
    string? faction = null,
    IReadOnlyList<string>? cardTypes = null,
    IReadOnlyList<string>? cardIds = null,
    bool? ownerIsOpponent = null) : IEffectGuard
{
    /// <summary>検査対象を指すセレクタ。</summary>
    public MatchSelector Selector => selector;

    /// <summary>必要な陣営。null の場合は陣営条件なし。</summary>
    public string? Faction => faction;

    /// <summary>許容するカードタイプ列。null の場合は条件なし。</summary>
    public IReadOnlyList<string>? CardTypes => cardTypes;

    /// <summary>許容するカード ID 列。null の場合は条件なし。</summary>
    public IReadOnlyList<string>? CardIds => cardIds;

    /// <summary>所有者が相手であることを要求するなら true、自身を要求するなら false、未指定なら null。</summary>
    public bool? OwnerIsOpponent => ownerIsOpponent;

    /// <inheritdoc />
    public bool Check(EffectContext ctx)
    {
        var card = ResolveCard(ctx);
        if (card is null) { return false; }

        if (faction is { Length: > 0 } && card.Faction != faction) { return false; }
        if (cardTypes is { Count: > 0 } && !EffectHelpers.MatchesAnyCardType(card, cardTypes)) { return false; }
        if (cardIds is { Count: > 0 } && !cardIds.Contains(card.CardId)) { return false; }
        if (ownerIsOpponent is { } expectOpponent && !CheckOwner(ctx, expectOpponent)) { return false; }

        return true;
    }

    private CardDefinition? ResolveCard(EffectContext ctx) => selector switch
    {
        MatchSelector.Target => ctx.Target is null ? null : ctx.CardCache.MustGet(ctx.Target.CardID),
        MatchSelector.EventCard => ctx.IncidentCard,
        MatchSelector.Attacker => ctx.Source is null ? null : ctx.CardCache.MustGet(ctx.Source.CardID),
        _ => throw new InvalidOperationException($"Unsupported match selector: {selector}"),
    };

    private static bool CheckOwner(EffectContext ctx, bool expectOpponent)
    {
        if (ctx.EventOwnerNum is not { } owner) { return false; }
        bool actualIsOpponent = owner != ctx.PlayerNum;
        return actualIsOpponent == expectOpponent;
    }
}

/// <summary>
/// 2 つの参照が同一インスタンスか検査する述語。
/// </summary>
public class SameGuard(ResourceRef a, ResourceRef b) : IEffectGuard
{
    /// <summary>比較する 1 つ目の参照。</summary>
    public ResourceRef A => a;

    /// <summary>比較する 2 つ目の参照。</summary>
    public ResourceRef B => b;

    /// <inheritdoc />
    public bool Check(EffectContext ctx)
    {
        string? idA = ResourceRefResolver.TryResolve(a, ctx);
        string? idB = ResourceRefResolver.TryResolve(b, ctx);
        return idA is not null && idB is not null && idA == idB;
    }
}

/// <summary>
/// 2 つの参照が別インスタンスか検査する述語。
/// </summary>
public class NotSameGuard(ResourceRef a, ResourceRef b) : IEffectGuard
{
    /// <summary>比較する 1 つ目の参照。</summary>
    public ResourceRef A => a;

    /// <summary>比較する 2 つ目の参照。</summary>
    public ResourceRef B => b;

    /// <inheritdoc />
    public bool Check(EffectContext ctx)
    {
        string? idA = ResourceRefResolver.TryResolve(a, ctx);
        string? idB = ResourceRefResolver.TryResolve(b, ctx);
        return idA is not null && idB is not null && idA != idB;
    }
}

/// <summary>
/// 対象の実効 AV が閾値以下か検査する述語。
/// </summary>
public class TargetAVGuard(long maxAV) : IEffectGuard
{
    /// <summary>対象の実効 AV の上限。</summary>
    public long MaxAV => maxAV;

    /// <inheritdoc />
    public bool Check(EffectContext ctx) =>
        ctx.Target is not null && ctx.Target.EffectiveAV <= maxAV;
}

/// <summary>
/// トリガーイベントを起こしたプレイヤーが、効果所有者自身か相手かを検査する述語。
/// </summary>
public class EventOwnerGuard(bool isSelf) : IEffectGuard
{
    /// <summary>true なら自身を要求、false なら相手を要求。</summary>
    public bool IsSelf => isSelf;

    /// <inheritdoc />
    public bool Check(EffectContext ctx)
    {
        if (ctx.EventOwnerNum is not { } owner) { return false; }
        bool ownerIsSelf = owner == ctx.PlayerNum;
        return ownerIsSelf == isSelf;
    }
}

/// <summary>
/// 宣言された攻撃ダメージが対象の現在の実効 AV 以上 (致死) か検査する述語。
/// </summary>
public class LethalGuard : IEffectGuard
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly LethalGuard Instance = new();

    private LethalGuard() { }

    /// <inheritdoc />
    public bool Check(EffectContext ctx) =>
        ctx.Target is not null
        && ctx.EventDamage is { } damage
        && damage >= ctx.Target.EffectiveAV;
}

/// <summary>
/// 内側の述語の判定結果を反転する述語。
/// </summary>
public class NegateGuard(IEffectGuard inner) : IEffectGuard
{
    /// <summary>反転対象の述語。</summary>
    public IEffectGuard Inner => inner;

    /// <inheritdoc />
    public bool Check(EffectContext ctx) => !inner.Check(ctx);
}

/// <summary>
/// 条件に一致するリソース (サポートゾーン含む) の数が範囲を満たすか検査する述語。
/// </summary>
public class ResourceCountGuard(
    string owner,
    string? zone,
    string? faction,
    List<string>? cardTypes,
    List<string>? cardIds,
    int? min,
    int? max) : IEffectGuard
{
    /// <summary>所有者フィルタ ("myself" / "opponent" / "both")。</summary>
    public string Owner => owner;

    /// <summary>ゾーンフィルタ (frontend / backend / support)。null なら全ゾーン。</summary>
    public string? Zone => zone;

    /// <summary>陣営フィルタ。null なら全陣営。</summary>
    public string? Faction => faction;

    /// <summary>カードタイプフィルタ。null なら全タイプ。</summary>
    public IReadOnlyList<string>? CardTypes => cardTypes;

    /// <summary>カード ID フィルタ。null なら全カード ID。</summary>
    public IReadOnlyList<string>? CardIds => cardIds;

    /// <summary>カウントの最低値。null なら下限なし。</summary>
    public int? Min => min;

    /// <summary>カウントの最大値。null なら上限なし。</summary>
    public int? Max => max;

    /// <inheritdoc />
    public bool Check(EffectContext ctx)
    {
        int count = CountResources(ctx);
        return (min is null || count >= min) && (max is null || count <= max);
    }

    private int CountResources(EffectContext ctx)
    {
        int total = 0;
        if (owner is "myself" or "both")
        {
            total += CountField(ctx.State.GetField(ctx.PlayerNum), ctx.CardCache);
        }
        if (owner is "opponent" or "both")
        {
            total += CountField(ctx.State.GetField(ctx.State.OpponentOf(ctx.PlayerNum)), ctx.CardCache);
        }
        return total;
    }

    private int CountField(Field field, ICardCache cc)
    {
        if (zone == Zones.Support)
        {
            return field.Support.Count(s =>
                s.FaceUp
                && s.DeployingTurnsLeft <= 0
                && MatchesFaction(s.CardID, cc)
                && MatchesCardTypes(s.CardID, cc)
                && MatchesCardIds(s.CardID));
        }

        IEnumerable<DeployedResource> candidates = zone switch
        {
            Zones.Frontend => field.Frontend,
            Zones.Backend => field.Backend,
            _ => field.Frontend.Concat(field.Backend),
        };

        return candidates
            .Where(r => r.FaceUp
                && MatchesFaction(r.CardID, cc)
                && MatchesCardTypes(r.CardID, cc)
                && MatchesCardIds(r.CardID))
            .Sum(r => (int)r.TemporaryEffects
                .Where(e => e.EffectType == "count_multiplier")
                .Select(e => e.Value)
                .DefaultIfEmpty(1)
                .Max());
    }

    private bool MatchesFaction(string cardID, ICardCache cc) =>
        faction is not { Length: > 0 } || cc.MustGet(cardID).Faction == faction;

    private bool MatchesCardTypes(string cardID, ICardCache cc) =>
        cardTypes is not { Count: > 0 } || EffectHelpers.MatchesAnyCardType(cc.MustGet(cardID), cardTypes);

    private bool MatchesCardIds(string cardID) =>
        cardIds is not { Count: > 0 } || cardIds.Contains(cardID);
}
