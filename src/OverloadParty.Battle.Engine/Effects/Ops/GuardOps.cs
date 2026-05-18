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
/// Identifies which resource or card a guard inspects.
/// </summary>
public enum MatchSelector
{
    /// <summary>The effect's target resource.</summary>
    Target,

    /// <summary>The card that raised the triggering event (e.g. the incident card).</summary>
    EventCard,

    /// <summary>The attack-declaring resource of an on_attack_declared event.</summary>
    Attacker,
}

/// <summary>
/// Verifies the resource or card identified by a selector matches the given
/// faction / card type / card id / owner criteria.
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
        bool isOpponent = owner != ctx.PlayerNum;
        if (isOpponent != expectOpponent)
        {
            throw new GameRuleException($"{selector} owner mismatch");
        }
    }
}

/// <summary>
/// Identifies a resource referenced by a reference-comparison guard (same / not_same).
/// </summary>
public enum ResourceRef
{
    /// <summary>The card that triggered the effect.</summary>
    Source,

    /// <summary>The effect's target resource.</summary>
    Target,

    /// <summary>The resource an Attachment source is equipped to.</summary>
    EquipHost,
}

/// <summary>
/// Resolves the instance ID of a <see cref="ResourceRef"/> within a pipeline context.
/// </summary>
internal static class ResourceRefResolver
{
    /// <summary>Resolves the instance ID of the given reference, throwing when it is unavailable.</summary>
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
/// Verifies two referenced resources are the same instance.
/// </summary>
public class GuardSameOp(ResourceRef a, ResourceRef b) : IEffectOp
{
    /// <summary>First reference being compared.</summary>
    public ResourceRef A => a;

    /// <summary>Second reference being compared.</summary>
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
/// Verifies two referenced resources are different instances.
/// </summary>
public class GuardNotSameOp(ResourceRef a, ResourceRef b) : IEffectOp
{
    /// <summary>First reference being compared.</summary>
    public ResourceRef A => a;

    /// <summary>Second reference being compared.</summary>
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
/// Verifies the player who caused the triggering event is self or opponent of the card holder.
/// </summary>
public class GuardEventOwnerOp(bool isSelf) : IEffectOp
{
    /// <summary>True when the event owner must be the card holder; false for the opponent.</summary>
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
/// Verifies the declared attack damage is at or above the target's current effective AV (lethal).
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
