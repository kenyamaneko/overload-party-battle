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
/// Prevents destruction by resetting damage so effective AV = surviveAV.
/// Also cancels the triggering action.
/// </summary>
public class SurviveDestructionOp(long surviveAV) : IEffectOp
{
    public long SurviveAV => surviveAV;

    public void Execute(OpContext ctx)
    {
        if (ctx.Target is null)
        {
            throw new GameRuleException("No target to protect");
        }

        ctx.Target.Damage = ctx.Target.MaxAV - surviveAV;
        if (ctx.Target.Damage < 0)
        {
            ctx.Target.Damage = 0;
        }
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
        {
            option = val?.ToString();
        }

        if (option is null || !branches.TryGetValue(option, out var ops))
        {
            throw new GameRuleException($"Invalid choice: {option}");
        }

        foreach (var op in ops)
        {
            op.Execute(ctx);
        }
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
        if (!cond(ctx)) { return; }

        foreach (var op in then)
        {
            op.Execute(ctx);
        }
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
