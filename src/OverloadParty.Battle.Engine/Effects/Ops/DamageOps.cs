using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// Deals damage to selected resources.
/// </summary>
public class DealDamageOp(ISelector sel, IAmountResolver value) : IEffectOp
{
    /// <summary>The selector used to pick target resources.</summary>
    public ISelector Selector => sel;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        long amount = value.Resolve(ctx);
        var targets = sel.Select(ctx);

        foreach (var target in targets)
        {
            target.Damage += amount;
        }
    }
}

/// <summary>
/// Deals incident damage to selected resources, with an optional budget penalty to the opponent.
/// </summary>
public class IncidentDamageOp(ISelector sel, IAmountResolver value, IAmountResolver? budgetPenalty = null) : IEffectOp
{
    /// <summary>The selector used to pick target resources.</summary>
    public ISelector Selector => sel;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        long damage = value.Resolve(ctx);
        var targets = sel.Select(ctx);

        foreach (var target in targets)
        {
            target.Damage += damage;
        }

        if (budgetPenalty is null) { return; }

        long penalty = budgetPenalty.Resolve(ctx);
        long oppBudget = ctx.State.GetBudget(ctx.OpponentNum);
        ctx.State.SetBudget(ctx.OpponentNum, oppBudget - penalty);
    }
}

/// <summary>
/// Heals (reduces) damage on selected resources.
/// </summary>
public class HealDamageOp(ISelector sel, IAmountResolver value) : IEffectOp
{
    /// <summary>The selector used to pick target resources.</summary>
    public ISelector Selector => sel;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        long amount = value.Resolve(ctx);
        var targets = sel.Select(ctx);

        foreach (var target in targets)
        {
            target.Damage = Math.Max(0, target.Damage - amount);
        }
    }
}

/// <summary>
/// Fully heals selected resources by setting damage to zero.
/// </summary>
public class FullHealOp(ISelector sel) : IEffectOp
{
    /// <summary>The selector used to pick target resources.</summary>
    public ISelector Selector => sel;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        var targets = sel.Select(ctx);

        foreach (var target in targets)
        {
            target.Damage = 0;
        }
    }
}
