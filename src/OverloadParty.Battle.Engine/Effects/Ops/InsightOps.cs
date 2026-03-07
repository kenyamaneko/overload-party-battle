namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// Adds insight to the effect owner's pool.
/// </summary>
public class GainInsightOp(IAmountResolver value) : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        long amount = value.Resolve(ctx);
        long pool = ctx.State.GetInsightPool(ctx.PlayerNum);
        ctx.State.SetInsightPool(ctx.PlayerNum, pool + amount);
    }
}

/// <summary>
/// Transfers insight from the opponent's pool to the effect owner's pool.
/// </summary>
public class AbsorbInsightOp(IAmountResolver value) : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        long amount = value.Resolve(ctx);
        long oppPool = ctx.State.GetInsightPool(ctx.OpponentNum);
        long absorbed = Math.Min(amount, oppPool);

        ctx.State.SetInsightPool(ctx.OpponentNum, oppPool - absorbed);
        long myPool = ctx.State.GetInsightPool(ctx.PlayerNum);
        ctx.State.SetInsightPool(ctx.PlayerNum, myPool + absorbed);
    }
}
