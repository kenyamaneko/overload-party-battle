using System.Linq;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// Cancels the triggering action (for reactive effects).
/// </summary>
public class SetCancelActionOp : IEffectOp
{
    public static readonly SetCancelActionOp Instance = new();
    public void Execute(OpContext ctx) => ctx.CancelAction();
}

/// <summary>
/// Fails if player's budget is below the minimum.
/// </summary>
public class RequireBudgetOp(long min) : IEffectOp
{
    public long Min => min;

    public void Execute(OpContext ctx)
    {
        long budget = ctx.State.GetBudget(ctx.PlayerNum);
        if (budget < min)
            throw new GameRuleException($"Insufficient budget: need {min}, have {budget}");
    }
}

/// <summary>
/// Fails if player's budget exceeds the maximum.
/// </summary>
public class RequireMaxBudgetOp(long max) : IEffectOp
{
    public long Max => max;

    public void Execute(OpContext ctx)
    {
        long budget = ctx.State.GetBudget(ctx.PlayerNum);
        if (budget > max)
            throw new GameRuleException($"Budget too high: max {max}, have {budget}");
    }
}

/// <summary>
/// Fails if faction card count on own field is below minimum.
/// </summary>
public class RequireFactionCountOp(string faction, int min) : IEffectOp
{
    public string Faction => faction;
    public int Min => min;

    public void Execute(OpContext ctx)
    {
        int count = EffectHelpers.CountFactionCards(ctx.MyField, faction, ctx.CardCache);
        if (count < min)
            throw new GameRuleException($"Need {min}+ {faction} cards on field, have {count}");
    }
}

/// <summary>
/// Fails if opponent has no backend resources.
/// </summary>
public class RequireOpponentBackendOp : IEffectOp
{
    public static readonly RequireOpponentBackendOp Instance = new();

    public void Execute(OpContext ctx)
    {
        int count = EffectHelpers.CountOpponentBackend(ctx.State, ctx.PlayerNum);
        if (count == 0)
            throw new GameRuleException("Opponent has no backend resources");
    }
}

/// <summary>
/// Verifies target matches a specific faction and optionally card type.
/// </summary>
public class GuardFactionOp(string faction, string? cardType = null) : IEffectOp
{
    public string Faction => faction;
    public string? CardType => cardType;

    public void Execute(OpContext ctx)
    {
        if (ctx.Target is null)
            throw new GameRuleException("No target");

        var card = ctx.CardCache.MustGet(ctx.Target.CardID);
        if (faction.Length > 0 && card.Faction != faction)
            throw new GameRuleException($"Target is not {faction} faction");
        if (cardType is { Length: > 0 } ct && card.CardType != ct)
        {
            // Also check category match (e.g., "data" matches Database/CacheDB/etc.)
            if (ct == "data" && !card.IsDataType)
                throw new GameRuleException($"Target is not data type");
            else if (ct == "compute" && !card.IsComputeType)
                throw new GameRuleException($"Target is not compute type");
            else if (ct != "data" && ct != "compute" && card.CardType != ct)
                throw new GameRuleException($"Target is not {ct} type");
        }
    }
}

/// <summary>
/// Verifies target is not the same instance as source.
/// </summary>
public class GuardNotSelfOp : IEffectOp
{
    public static readonly GuardNotSelfOp Instance = new();

    public void Execute(OpContext ctx)
    {
        if (ctx.Source is null || ctx.Target is null)
            throw new GameRuleException("Source or target missing");
        if (ctx.Source.InstanceID == ctx.Target.InstanceID)
            throw new GameRuleException("Target must be different from source");
    }
}

/// <summary>
/// Verifies target's effective AV is at or below a threshold.
/// </summary>
public class GuardTargetAVOp(long maxAV) : IEffectOp
{
    public long MaxAV => maxAV;

    public void Execute(OpContext ctx)
    {
        if (ctx.Target is null)
            throw new GameRuleException("No target");
        if (ctx.Target.EffectiveAV > maxAV)
            throw new GameRuleException($"Target AV {ctx.Target.EffectiveAV} exceeds max {maxAV}");
    }
}

/// <summary>
/// Prevents destruction by resetting damage so effective AV = surviveAV.
/// Also cancels the triggering action.
/// </summary>
public class SurviveDestructionOp(long surviveAV) : IEffectOp
{
    public long SurviveAV => surviveAV;

    public void Execute(OpContext ctx)
    {
        if (ctx.Target is null)
            throw new GameRuleException("No target to protect");

        ctx.Target.Damage = ctx.Target.MaxAV - surviveAV;
        if (ctx.Target.Damage < 0)
            ctx.Target.Damage = 0;
        ctx.CancelAction();
    }
}

/// <summary>
/// Reads player choice and dispatches to the corresponding op sequence.
/// </summary>
public class BranchOnChoiceOp(Dictionary<string, List<IEffectOp>> branches) : IEffectOp
{
    public Dictionary<string, List<IEffectOp>> Branches => branches;

    public void Execute(OpContext ctx)
    {
        string? option = null;
        if (ctx.ChoiceData?.TryGetValue("option", out var val) == true)
            option = val?.ToString();

        if (option is null || !branches.TryGetValue(option, out var ops))
            throw new GameRuleException($"Invalid choice: {option}");

        foreach (var op in ops)
            op.Execute(ctx);
    }
}

/// <summary>
/// Runs ops only if condition is true; otherwise silently skips (no error).
/// </summary>
public class IfConditionOp(Func<OpContext, bool> cond, List<IEffectOp> then) : IEffectOp
{
    public Func<OpContext, bool> Cond => cond;
    public List<IEffectOp> Then => then;

    public void Execute(OpContext ctx)
    {
        if (!cond(ctx)) return;
        foreach (var op in then)
            op.Execute(ctx);
    }
}

/// <summary>
/// Escape hatch for effects that cannot be decomposed into standard ops.
/// </summary>
public class CustomFnOp(Action<OpContext> fn) : IEffectOp
{
    public void Execute(OpContext ctx) => fn(ctx);
}

/// <summary>
/// Custom function with NPC classification metadata.
/// </summary>
public class CustomFnTaggedOp(Action<OpContext> fn) : IEffectOp
{
    public List<EffectCategory> Categories { get; init; } = [];
    public EffectTargetType Target { get; init; } = EffectTargetType.None;
    public string? Zone { get; init; }

    public void Execute(OpContext ctx) => fn(ctx);
}

/// <summary>
/// Deploys the first matching card from the repository to an empty field slot.
/// </summary>
public class DeployFromRepoOp : IEffectOp
{
    public Func<CardDefinition, bool>? Filter { get; init; }
    public long OverrideAV { get; init; }

    public void Execute(OpContext ctx)
    {
        var repo = ctx.State.GetRepository(ctx.PlayerNum);

        var match = repo.FirstOrDefault(candidate =>
        {
            var definition = ctx.CardCache.Get(candidate.CardID);
            if (definition is null) return false;
            if (Filter is not null && !Filter(definition)) return false;
            return true;
        });

        if (match is null) return;

        repo.Remove(match);
        var cardDef = ctx.CardCache.MustGet(match.CardID);
        var field = ctx.GetField(ctx.PlayerNum);
        var instance = FieldHelpers.CreateResourceInstance(cardDef, ctx.State.NextInstanceID(), ctx.State.CurrentTurn);

        if (OverrideAV > 0)
        {
            instance.MaxAV = OverrideAV;
            instance.Damage = 0;
        }

        EffectHelpers.PlaceResourceOnField(field, instance, cardDef.CardType);
    }
}

/// <summary>
/// Deploys a card from hand by player choice.
/// </summary>
public class DeployFromHandOp : IEffectOp
{
    public Func<CardDefinition, bool>? Filter { get; init; }

    public void Execute(OpContext ctx)
    {
        long? choiceCardNo = null;
        if (ctx.ChoiceData?.TryGetValue("cardNo", out var val) == true)
        {
            if (val is long l) choiceCardNo = l;
            else if (val is int i) choiceCardNo = i;
            else if (long.TryParse(val?.ToString(), out var parsed)) choiceCardNo = parsed;
        }

        if (choiceCardNo is null)
            throw new GameRuleException("No card chosen for deploy from hand");

        if (Filter is not null)
        {
            var card = ctx.CardCache.Get(choiceCardNo.Value);
            if (card is null || !Filter(card))
                throw new GameRuleException($"Card {choiceCardNo} does not match filter");
        }

        EffectHelpers.DeployResourceFromHand(ctx.State, ctx.PlayerNum, choiceCardNo.Value, ctx.CardCache, ctx);
    }
}

/// <summary>
/// Deploys the same card as the current target from the repository.
/// </summary>
public class DeployFromRepoSameCardOp(long overrideAV = 0) : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        if (ctx.Target is null)
            throw new GameRuleException("No target for same card deploy");

        var inner = new DeployFromRepoOp
        {
            Filter = card => card.CardNo == ctx.Target.CardID,
            OverrideAV = overrideAV,
        };
        inner.Execute(ctx);
    }
}
