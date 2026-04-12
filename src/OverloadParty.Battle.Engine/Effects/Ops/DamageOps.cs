using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// DealDamageOp は選択されたリソースにダメージを与えます
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
            if (FieldHelpers.HasTemporaryEffect(target, BuffTypes.IncidentImmune))
            {
                continue;
            }

            long effectiveDamage = FieldHelpers.ApplyReduction(
                target.TemporaryEffects, BuffTypes.IncidentReduction, damage);

            target.Damage += effectiveDamage;
        }

        if (budgetPenalty is null) { return; }

        long penalty = budgetPenalty.Resolve(ctx);
        long oppBudget = ctx.State.GetBudget(ctx.OpponentNum);
        ctx.State.SetBudget(ctx.OpponentNum, oppBudget - penalty);
    }
}

/// <summary>
/// ヒールDamageOp は選択されたリソースのダメージを回復（減少）します
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
/// FullHealOp は選択されたリソースのダメージをゼロにして完全回復します
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
