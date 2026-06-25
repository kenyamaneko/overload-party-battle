using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// SetCancelActionOp はトリガーとなったアクションをキャンセルします（リアクティブ効果用）
/// </summary>
public class SetCancelActionOp : IEffectOp
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly SetCancelActionOp Instance = new();

    /// <inheritdoc />
    public void Execute(OpContext ctx) => ctx.CancelAction();
}

/// <summary>
/// Prevents destruction by resetting damage so effective AV = surviveAV.
/// Also cancels the triggering action.
/// </summary>
public class SurviveDestructionOp(long surviveAV) : IEffectOp
{
    /// <summary>The AV the target will be left with after surviving.</summary>
    public long SurviveAV => surviveAV;

    /// <inheritdoc />
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
/// BranchOnChoiceOp はプレイヤーの選択を読み取り対応する Op シーケンスにディスパッチします
/// </summary>
public class BranchOnChoiceOp(Dictionary<string, List<IEffectOp>> branches) : IEffectOp
{
    /// <summary>Map of choice option keys to their op sequences.</summary>
    public Dictionary<string, List<IEffectOp>> Branches => branches;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        string? option = null;
        if (ctx.ChoiceData?.TryGetValue("option", out var val) == true)
        {
            option = val?.ToString();
        }

        if (option is null)
        {
            ctx.SuspendForChoice("option", ChoiceKinds.Branch, branches.Keys.ToList(), ctx.PlayerNum);
            return;
        }

        if (!branches.TryGetValue(option, out var ops))
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
    /// <summary>The condition predicate.</summary>
    public Func<OpContext, bool> Cond => cond;

    /// <summary>Ops to execute when the condition is true.</summary>
    public List<IEffectOp> Then => then;

    /// <inheritdoc />
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
    /// <inheritdoc />
    public void Execute(OpContext ctx) => fn(ctx);
}

/// <summary>
/// Guards against re-use of an effect based on per-turn or per-game limit.
/// </summary>
public class CheckUseLimitOp(bool perGame) : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (perGame)
        {
            bool used = (ctx.Source?.EffectUsedThisGame ?? false) || (ctx.SupSource?.EffectUsedThisGame ?? false);
            if (used)
            {
                throw new GameRuleException("Effect already used this game");
            }
        }
        else
        {
            bool used = (ctx.Source?.EffectUsedThisTurn ?? false) || (ctx.SupSource?.EffectUsedThisTurn ?? false);
            if (used)
            {
                throw new GameRuleException("Effect already used this turn");
            }
        }
    }
}

/// <summary>
/// Marks the source as having used its effect (per-turn or per-game).
/// </summary>
public class MarkUseLimitOp(bool perGame) : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (perGame)
        {
            if (ctx.Source is not null)
            {
                ctx.Source.EffectUsedThisGame = true;
            }

            if (ctx.SupSource is not null)
            {
                ctx.SupSource.EffectUsedThisGame = true;
            }
        }
        else
        {
            if (ctx.Source is not null)
            {
                ctx.Source.EffectUsedThisTurn = true;
            }

            if (ctx.SupSource is not null)
            {
                ctx.SupSource.EffectUsedThisTurn = true;
            }
        }
    }
}

/// <summary>
/// Custom function with NPC classification metadata.
/// </summary>
public class CustomFnTaggedOp(Action<OpContext> fn) : IEffectOp
{
    /// <summary>Effect categories for NPC classification.</summary>
    public List<EffectCategory> Categories { get; init; } = [];

    /// <summary>Target type hint for NPC classification.</summary>
    public EffectTargetType Target { get; init; } = EffectTargetType.None;

    /// <summary>Zone hint for NPC classification.</summary>
    public string? Zone { get; init; }

    /// <inheritdoc />
    public void Execute(OpContext ctx) => fn(ctx);
}
