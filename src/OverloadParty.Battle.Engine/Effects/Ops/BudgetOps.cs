using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// Identifies which player an operation targets.
/// </summary>
public enum PlayerRef { Self, Opponent, Both }

/// <summary>
/// Adds budget to a player.
/// </summary>
public class GainBudgetOp(PlayerRef player, IAmountResolver value) : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        long amount = value.Resolve(ctx);
        long playerNum = player == PlayerRef.Self ? ctx.PlayerNum : ctx.OpponentNum;
        long budget = ctx.State.GetBudget(playerNum);
        ctx.State.SetBudget(playerNum, budget + amount);
    }
}

/// <summary>
/// Subtracts budget from a player.
/// </summary>
public class LoseBudgetOp(PlayerRef player, IAmountResolver value) : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        long amount = value.Resolve(ctx);
        long playerNum = player == PlayerRef.Self ? ctx.PlayerNum : ctx.OpponentNum;
        long budget = ctx.State.GetBudget(playerNum);
        ctx.State.SetBudget(playerNum, budget - amount);
    }
}
