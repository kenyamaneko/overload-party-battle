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
            if (ctx.Result.PendingChoice is not null) { return; }
            if (ctx.Result.HasGuardFailed) { return; }
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
            if (ctx.Result.PendingChoice is not null) { return; }
            if (ctx.Result.HasGuardFailed) { return; }
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
/// カード記載の回数制限を使い切った効果の実行を止めます。
/// </summary>
public class CheckUseLimitOp(UseLimitKind limit) : IEffectOp
{
    /// <summary>この op が確かめる回数制限。</summary>
    public UseLimitKind Limit => limit;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        bool used = UseLimitRules.IsConsumed(
            limit, ctx.State, ctx.PlayerNum, ctx.Ctx.EffectCardId, ctx.Source, ctx.SupSource);
        if (!used)
        {
            return;
        }

        // 起動効果はプレイヤーが自分で使用を選ぶため、使用済みでの到達は不正なリクエストとして例外にする。
        // 誘発効果は契機イベントが勝手に来るため、使用済みは発動条件の不成立として扱い契機のアクションを拒否しない。
        if (ctx.Ctx.Trigger == TriggerType.Ignition)
        {
            throw new GameRuleException(limit switch
            {
                UseLimitKind.OncePerGame => "Effect already used this game",
                UseLimitKind.OncePerTurn => "Effect already used this turn",
                _ => throw new ArgumentOutOfRangeException(nameof(limit), limit, "unknown use limit"),
            });
        }

        ctx.AbortAsConditionUnmet();
    }
}

/// <summary>
/// カード記載の回数制限を 1 回分使ったものとして記録します。
/// </summary>
public class MarkUseLimitOp(UseLimitKind limit) : IEffectOp
{
    /// <summary>この op が記録する回数制限。</summary>
    public UseLimitKind Limit => limit;

    /// <inheritdoc />
    public void Execute(OpContext ctx) => UseLimitRules.MarkConsumed(
        limit, ctx.State, ctx.PlayerNum, ctx.Ctx.EffectCardId, ctx.Source, ctx.SupSource);
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

    /// <summary>リソースを置くスロットを要求する効果かどうか。</summary>
    public bool RequiresPlacementSlot { get; init; }

    /// <inheritdoc />
    public void Execute(OpContext ctx) => fn(ctx);
}
