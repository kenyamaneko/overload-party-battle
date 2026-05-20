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
