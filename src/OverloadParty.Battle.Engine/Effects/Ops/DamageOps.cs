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
            DamageApplication.Apply(ctx, target, amount);
        }

        foreach (var evt in DestructionSweep.Run(ctx.State, ctx.Game, ctx.CardCache, ctx.Effects))
        {
            ctx.AddEvent(evt);
        }
    }
}

/// <summary>
/// 効果処理におけるリソースへのダメージ適用と on_damaged 発火の集約点です
/// </summary>
internal static class DamageApplication
{
    /// <summary>
    /// リソースにダメージを与え、on_damaged を発火します
    /// </summary>
    /// <param name="ctx">パイプライン実行コンテキスト。</param>
    /// <param name="target">ダメージ適用先のリソース。</param>
    /// <param name="amount">適用するダメージ量。</param>
    public static void Apply(OpContext ctx, DeployedResource target, long amount)
    {
        long owner = ctx.OwnerOf(target)
            ?? throw new GameRuleException($"Damage target {target.InstanceID} is on neither field");

        var events = ResourceHelpers.ApplyDamage(
            ctx.State, ctx.Game, ctx.CardCache, ctx.Effects, target, owner, amount);
        foreach (var evt in events)
        {
            ctx.AddEvent(evt);
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

            DamageApplication.Apply(ctx, target, effectiveDamage);
        }

        foreach (var evt in DestructionSweep.Run(ctx.State, ctx.Game, ctx.CardCache, ctx.Effects))
        {
            ctx.AddEvent(evt);
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
