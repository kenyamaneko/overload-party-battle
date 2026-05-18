using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// Wraps a named group of ops so that guard failures (GameRuleException) are caught,
/// allowing subsequent independent groups in the same pipeline to run.
/// The success/failure result is recorded in <see cref="OpContext.GroupResults"/>.
/// </summary>
public class EffectGroupOp(string groupId, IEffectOp[] ops) : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        try
        {
            foreach (var op in ops)
            {
                op.Execute(ctx);
            }

            ctx.GroupResults[groupId] = true;
        }
        catch (GameRuleException)
        {
            ctx.GroupResults[groupId] = false;
        }
    }
}

/// <summary>
/// Runs child ops only if the parent group succeeded.
/// Guard failures in the child ops are silently swallowed.
/// </summary>
public class DependentEffectOp(string parentGroupId, IEffectOp[] ops) : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        if (!ctx.GroupResults.GetValueOrDefault(parentGroupId))
        {
            return;
        }

        try
        {
            foreach (var op in ops)
            {
                op.Execute(ctx);
            }
        }
        catch (GameRuleException)
        {
            // 従属グループのガード失敗は無視される
        }
    }
}

/// <summary>
/// Records that a group succeeded in <see cref="OpContext.GroupResults"/>.
/// Used when the root block runs without exception isolation (single independent + dependents).
/// </summary>
public class MarkGroupSucceededOp(string groupId) : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        ctx.GroupResults[groupId] = true;
    }
}
