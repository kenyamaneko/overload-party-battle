using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

public class DealDamageOp(ISelector sel, IAmountResolver value) : IEffectOp
{
    public ISelector Selector => sel;

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

public class IncidentDamageOp(ISelector sel, IAmountResolver value, IAmountResolver? budgetPenalty = null) : IEffectOp
{
    public ISelector Selector => sel;

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

public class HealDamageOp(ISelector sel, IAmountResolver value) : IEffectOp
{
    public ISelector Selector => sel;

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

public class FullHealOp(ISelector sel) : IEffectOp
{
    public ISelector Selector => sel;

    public void Execute(OpContext ctx)
    {
        var targets = sel.Select(ctx);

        foreach (var target in targets)
        {
            target.Damage = 0;
        }
    }
}
