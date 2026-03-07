using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// Fails if player's budget is below the minimum.
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
/// Fails if player's budget exceeds the maximum.
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
/// Fails if faction card count on own field is below minimum.
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
/// Fails if opponent has no backend resources.
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
/// Verifies target matches a specific faction and optionally card type.
/// </summary>
public class GuardFactionOp(string faction, string? cardType = null) : IEffectOp
{
    /// <summary>Required faction.</summary>
    public string Faction => faction;

    /// <summary>Required card type category, or null for any.</summary>
    public string? CardType => cardType;

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
        if (cardType is { Length: > 0 } ct && card.CardType != ct)
        {
            // Also check category match (e.g., "data" matches Database/CacheDB/etc.)
            if (ct == "data" && !card.IsDataType)
            {
                throw new GameRuleException($"Target is not data type");
            }
            else if (ct == "compute" && !card.IsComputeType)
            {
                throw new GameRuleException($"Target is not compute type");
            }
            else if (ct != "data" && ct != "compute" && card.CardType != ct)
            {
                throw new GameRuleException($"Target is not {ct} type");
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
