using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// PlayerRef は操作の対象プレイヤーを識別します
/// </summary>
public enum PlayerRef { Myself, Opponent, Both }

/// <summary>
/// GainBudgetOp はプレイヤーのバジェットを増加させます
/// </summary>
public class GainBudgetOp(PlayerRef player, IAmountResolver value) : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        long amount = value.Resolve(ctx);
        long playerNum = player == PlayerRef.Myself ? ctx.PlayerNum : ctx.OpponentNum;
        long budget = ctx.State.GetBudget(playerNum);
        ctx.State.SetBudget(playerNum, budget + amount);
    }
}

/// <summary>
/// LoseBudgetOp はプレイヤーのバジェットを減少させます
/// </summary>
public class LoseBudgetOp(PlayerRef player, IAmountResolver value) : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        long amount = value.Resolve(ctx);
        long playerNum = player == PlayerRef.Myself ? ctx.PlayerNum : ctx.OpponentNum;
        long budget = ctx.State.GetBudget(playerNum);
        ctx.State.SetBudget(playerNum, budget - amount);
    }
}

/// <summary>
/// ConvertInsightOp は効果所有者の Insight プール全量を ratePercent 倍の Budget に変換し、プールを 0 にします。
/// </summary>
/// <param name="ratePercent">Insight を Budget に変換する倍率 (パーセント)。</param>
public class ConvertInsightOp(long ratePercent) : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        long pool = ctx.State.GetInsightPool(ctx.PlayerNum);
        ctx.State.SetBudget(ctx.PlayerNum, ctx.State.GetBudget(ctx.PlayerNum) + pool * ratePercent / 100);
        ctx.State.SetInsightPool(ctx.PlayerNum, 0);
    }
}
