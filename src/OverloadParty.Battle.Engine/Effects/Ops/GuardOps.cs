using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// RequireBudgetOp はプレイヤーのバジェットが最低値未満の場合に失敗します
/// </summary>
public class RequireBudgetOp(long min) : IEffectOp
{
    /// <summary>Minimum budget required.</summary>
    public long Min => min;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        long budget = ctx.State.GetBudget(ctx.PlayerNum);
        if (budget < min)
        {
            throw new GameRuleException($"Insufficient budget: need {min}, have {budget}");
        }
    }
}

/// <summary>
/// RequireMaxBudgetOp はプレイヤーのバジェットが最大値を超える場合に失敗します
/// </summary>
public class RequireMaxBudgetOp(long max) : IEffectOp
{
    /// <summary>Maximum budget allowed.</summary>
    public long Max => max;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        long budget = ctx.State.GetBudget(ctx.PlayerNum);
        if (budget > max)
        {
            throw new GameRuleException($"Budget too high: max {max}, have {budget}");
        }
    }
}

/// <summary>
/// RequireFactionCountOp はフィールド上のファクションカード数が最低値未満の場合に失敗します
/// </summary>
public class RequireFactionCountOp(string faction, int min) : IEffectOp
{
    /// <summary>Faction to count.</summary>
    public string Faction => faction;

    /// <summary>Minimum number of cards required.</summary>
    public int Min => min;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        int count = EffectHelpers.CountFactionCards(ctx.MyField, faction, ctx.CardCache);
        if (count < min)
        {
            throw new GameRuleException($"Need {min}+ {faction} cards on field, have {count}");
        }
    }
}

/// <summary>
/// RequireOpponentBackendOp は相手がバックエンドリソースを持たない場合に失敗します
/// </summary>
public class RequireOpponentBackendOp : IEffectOp
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly RequireOpponentBackendOp Instance = new();

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        int count = EffectHelpers.CountOpponentBackend(ctx.State, ctx.PlayerNum);
        if (count == 0)
        {
            throw new GameRuleException("Opponent has no backend resources");
        }
    }
}

/// <summary>
/// guard が検査するリソースまたはカードの指定先。
/// </summary>
public enum MatchSelector
{
    /// <summary>効果の対象リソース。</summary>
    Target,

    /// <summary>トリガー event を発生させたカード（インシデントカード等）。</summary>
    EventCard,

    /// <summary>on_attack_declared で攻撃を宣言したリソース。</summary>
    Attacker,
}

/// <summary>
/// セレクタが指すリソース／カードが faction / card type / card id / owner の条件に一致するか検証します。
/// </summary>
public class GuardMatchOp(
    MatchSelector selector,
    string? faction = null,
    IReadOnlyList<string>? cardTypes = null,
    IReadOnlyList<string>? cardIds = null,
    bool? ownerIsOpponent = null) : IEffectOp
{
    /// <summary>The subject the guard inspects.</summary>
    public MatchSelector Selector => selector;

    /// <summary>Required faction, or null for any.</summary>
    public string? Faction => faction;

    /// <summary>Accepted card types, or null for any.</summary>
    public IReadOnlyList<string>? CardTypes => cardTypes;

    /// <summary>Accepted card IDs, or null for any.</summary>
    public IReadOnlyList<string>? CardIds => cardIds;

    /// <summary>When set, whether the subject must belong to the card holder's opponent.</summary>
    public bool? OwnerIsOpponent => ownerIsOpponent;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        var card = ResolveCard(ctx);

        if (faction is { Length: > 0 } && card.Faction != faction)
        {
            throw new GameRuleException($"{selector} is not {faction} faction");
        }
        if (cardTypes is { Count: > 0 } && !EffectHelpers.MatchesAnyCardType(card, cardTypes))
        {
            throw new GameRuleException($"{selector} is not one of {string.Join("/", cardTypes)} type");
        }
        if (cardIds is { Count: > 0 } && !cardIds.Contains(card.CardId))
        {
            throw new GameRuleException($"{selector} card id {card.CardId} not in allowed set");
        }
        if (ownerIsOpponent is { } expectOpponent)
        {
            CheckOwner(ctx, expectOpponent);
        }
    }

    private CardDefinition ResolveCard(OpContext ctx) => selector switch
    {
        MatchSelector.Target => ctx.CardCache.MustGet(
            (ctx.Target ?? throw new GameRuleException("No target")).CardID),
        MatchSelector.EventCard => ctx.EventCard
            ?? throw new GameRuleException("No event card in context"),
        MatchSelector.Attacker => ctx.CardCache.MustGet(
            (ctx.Attacker ?? throw new GameRuleException("No attacker in context")).CardID),
        _ => throw new GameRuleException($"Unsupported match selector: {selector}"),
    };

    private void CheckOwner(OpContext ctx, bool expectOpponent)
    {
        if (ctx.EventOwnerNum is not { } owner)
        {
            throw new GameRuleException($"No event owner for {selector} match guard");
        }
        bool actualIsOpponent = owner != ctx.PlayerNum;
        if (actualIsOpponent != expectOpponent)
        {
            throw new GameRuleException($"{selector} owner mismatch");
        }
    }
}

/// <summary>
/// 参照比較 guard（same / not_same）が指すリソースの指定先。
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
}

/// <summary>
/// 2 つの参照が同一インスタンスか検証します。
/// </summary>
public class GuardSameOp(ResourceRef a, ResourceRef b) : IEffectOp
{
    /// <summary>比較する 1 つ目の参照。</summary>
    public ResourceRef A => a;

    /// <summary>比較する 2 つ目の参照。</summary>
    public ResourceRef B => b;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (ResourceRefResolver.Resolve(a, ctx) != ResourceRefResolver.Resolve(b, ctx))
        {
            throw new GameRuleException($"{a} and {b} must be the same resource");
        }
    }
}

/// <summary>
/// 2 つの参照が別インスタンスか検証します。
/// </summary>
public class GuardNotSameOp(ResourceRef a, ResourceRef b) : IEffectOp
{
    /// <summary>比較する 1 つ目の参照。</summary>
    public ResourceRef A => a;

    /// <summary>比較する 2 つ目の参照。</summary>
    public ResourceRef B => b;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (ResourceRefResolver.Resolve(a, ctx) == ResourceRefResolver.Resolve(b, ctx))
        {
            throw new GameRuleException($"{a} and {b} must be different resources");
        }
    }
}

/// <summary>
/// Verifies target's effective AV is at or below a threshold.
/// </summary>
public class GuardTargetAVOp(long maxAV) : IEffectOp
{
    /// <summary>Maximum AV threshold the target must be at or below.</summary>
    public long MaxAV => maxAV;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (ctx.Target is null)
        {
            throw new GameRuleException("No target");
        }
        if (ctx.Target.EffectiveAV > maxAV)
        {
            throw new GameRuleException($"Target AV {ctx.Target.EffectiveAV} exceeds max {maxAV}");
        }
    }
}

/// <summary>
/// トリガーとなった event を起こしたプレイヤーが、カード保有者自身か相手かを検証します
/// </summary>
public class GuardEventOwnerOp(bool isSelf) : IEffectOp
{
    /// <summary>
    /// true なら event を起こしたのがカード保有者自身であることを要求し、false なら相手であることを要求します
    /// </summary>
    public bool IsSelf => isSelf;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (ctx.EventOwnerNum is not { } owner)
        {
            throw new GameRuleException("No event owner");
        }
        bool ownerIsSelf = owner == ctx.PlayerNum;
        if (ownerIsSelf != isSelf)
        {
            throw new GameRuleException(
                $"Event owner is {(ownerIsSelf ? "self" : "opponent")}, expected {(isSelf ? "self" : "opponent")}");
        }
    }
}

/// <summary>
/// 宣言された攻撃ダメージが対象の現在の実効 AV 以上（致死）か検証します。
/// </summary>
public class GuardLethalOp : IEffectOp
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly GuardLethalOp Instance = new();

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (ctx.Target is null)
        {
            throw new GameRuleException("No target for lethal guard");
        }
        if (ctx.EventDamage is not { } damage)
        {
            throw new GameRuleException("No attack damage in context");
        }
        if (damage < ctx.Target.EffectiveAV)
        {
            throw new GameRuleException(
                $"Attack damage {damage} below target AV {ctx.Target.EffectiveAV}");
        }
    }
}

/// <summary>
/// 内側の guard op の判定結果を反転します。
/// </summary>
public class NegateGuardOp(IEffectOp inner) : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        try
        {
            inner.Execute(ctx);
        }
        catch (GameRuleException)
        {
            return; // 内部が失敗 → 否定ガードは通過
        }
        throw new GameRuleException("Negated guard: inner condition was true");
    }
}

/// <summary>
/// 条件に一致するリソース（サポートゾーン含む）の数が範囲を満たさないとき失敗する汎用の数量 guard。
/// </summary>
public class ResourceCountGuardOp(
    string owner,
    string? zone,
    string? faction,
    List<string>? cardTypes,
    List<string>? cardIds,
    int min,
    int? max,
    bool negate) : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        int count = CountResources(ctx);
        bool satisfied = count >= min && (max is null || count <= max);
        if (negate)
        {
            satisfied = !satisfied;
        }

        if (!satisfied)
        {
            throw new GameRuleException($"Resource count guard failed: count={count}, min={min}, max={max}, negate={negate}");
        }
    }

    private int CountResources(OpContext ctx)
    {
        int total = 0;

        if (owner is "self" or "both")
        {
            total += CountField(ctx.MyField, ctx.CardCache, ctx.Source);
        }
        if (owner is "opponent" or "both")
        {
            total += CountField(ctx.OpponentField, ctx.CardCache, ctx.Source);
        }

        return total;
    }

    private int CountField(Field field, ICardCache cc, DeployedResource? source)
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

    private bool MatchesFaction(string cardID, ICardCache cc)
    {
        if (faction is not { Length: > 0 })
        {
            return true;
        }

        return cc.MustGet(cardID).Faction == faction;
    }

    private bool MatchesCardTypes(string cardID, ICardCache cc)
    {
        if (cardTypes is not { Count: > 0 })
        {
            return true;
        }

        return EffectHelpers.MatchesAnyCardType(cc.MustGet(cardID), cardTypes);
    }

    private bool MatchesCardIds(string cardID)
    {
        if (cardIds is not { Count: > 0 })
        {
            return true;
        }

        return cardIds.Contains(cardID);
    }
}
