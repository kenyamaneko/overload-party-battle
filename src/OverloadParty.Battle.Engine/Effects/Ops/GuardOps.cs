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
/// Verifies target matches a specific faction and optionally one of several card types.
/// </summary>
public class GuardFactionOp(string faction, IReadOnlyList<string>? cardTypes = null) : IEffectOp
{
    /// <summary>Required faction.</summary>
    public string Faction => faction;

    /// <summary>Accepted card types, or null for any.</summary>
    public IReadOnlyList<string>? CardTypes => cardTypes;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (ctx.Target is null)
        {
            throw new GameRuleException("No target");
        }

        var card = ctx.CardCache.MustGet(ctx.Target.CardID);
        if (faction.Length > 0 && card.Faction != faction)
        {
            throw new GameRuleException($"Target is not {faction} faction");
        }
        // 各値は category (Compute/Data/Platform...) または subtype (VM/Container/Database...)
        // どちらでも受け付けるため dual-match。"data"/"compute" lowercase は EffectYamlLoader が
        // category 名にエイリアスする想定だが、念のため受け付ける。
        if (cardTypes is { Count: > 0 })
        {
            var normalized = cardTypes.Select(ct => ct switch
            {
                "data" => OverloadParty.GameDesignConstants.CardTypes.Data,
                "compute" => OverloadParty.GameDesignConstants.CardTypes.Compute,
                _ => ct,
            }).ToList();
            if (!EffectHelpers.MatchesAnyCardType(card, normalized))
            {
                throw new GameRuleException($"Target is not one of {string.Join("/", cardTypes)} type");
            }
        }
    }
}

/// <summary>
/// Verifies target is not the same instance as source.
/// </summary>
public class GuardNotSelfOp : IEffectOp
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly GuardNotSelfOp Instance = new();

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (ctx.Source is null || ctx.Target is null)
        {
            throw new GameRuleException("Source or target missing");
        }
        if (ctx.Source.InstanceID == ctx.Target.InstanceID)
        {
            throw new GameRuleException("Target must be different from source");
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
/// Verifies the incident card used in an on_incident event is one of the given card IDs.
/// </summary>
public class GuardIncidentOp(IReadOnlyList<string> cardIds) : IEffectOp
{
    /// <summary>Card IDs the triggering incident must match one of.</summary>
    public IReadOnlyList<string> CardIds => cardIds;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (ctx.IncidentCard is not { } incident)
        {
            throw new GameRuleException("No incident in context");
        }
        if (!cardIds.Contains(incident.CardId))
        {
            throw new GameRuleException($"Incident {incident.CardId} not in allowed set");
        }
    }
}

/// <summary>
/// Verifies the attack-declaring resource matches owner / faction / card type.
/// </summary>
public class GuardAttackerOp(bool? ownerIsOpponent, string? faction, string? cardType) : IEffectOp
{
    /// <summary>When set, whether the attacker must belong to the opponent of the card holder.</summary>
    public bool? OwnerIsOpponent => ownerIsOpponent;

    /// <summary>Required attacker faction, or null for any.</summary>
    public string? Faction => faction;

    /// <summary>Required attacker card type, or null for any.</summary>
    public string? CardType => cardType;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (ctx.Attacker is not { } attacker)
        {
            throw new GameRuleException("No attacker in context");
        }

        if (ownerIsOpponent is { } expectOpponent)
        {
            if (ctx.EventOwnerNum is not { } owner)
            {
                throw new GameRuleException("No event owner for attacker guard");
            }
            bool isOpponent = owner != ctx.PlayerNum;
            if (isOpponent != expectOpponent)
            {
                throw new GameRuleException("Attacker owner mismatch");
            }
        }

        var card = ctx.CardCache.MustGet(attacker.CardID);
        if (faction is { Length: > 0 } && card.Faction != faction)
        {
            throw new GameRuleException($"Attacker is not {faction} faction");
        }
        if (cardType is { Length: > 0 } ct && !EffectHelpers.MatchesCardType(card, ct))
        {
            throw new GameRuleException($"Attacker is not {ct} type");
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

/// <summary>
/// Verifies this Attachment's equip host is the resource targeted by the triggering event.
/// </summary>
public class GuardEquipHostIsTargetOp : IEffectOp
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly GuardEquipHostIsTargetOp Instance = new();

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (ctx.SupSource?.TargetInstanceID is not { } hostId)
        {
            throw new GameRuleException("Source is not an attachment with an equip host");
        }
        if (ctx.Target is null)
        {
            throw new GameRuleException("No event target");
        }
        if (hostId != ctx.Target.InstanceID)
        {
            throw new GameRuleException("Equip host is not the event target");
        }
    }
}
