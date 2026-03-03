using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

public enum PlayerRef { Self, Opponent }

public class GainBudgetOp(PlayerRef player, IAmountResolver value) : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        long amount = value.Resolve(ctx);
        long playerNum = player == PlayerRef.Self ? ctx.PlayerNum : ctx.OpponentNum;
        long budget = ctx.State.GetBudget(playerNum);
        ctx.State.SetBudget(playerNum, budget + amount);
    }
}

public class LoseBudgetOp(PlayerRef player, IAmountResolver value) : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        long amount = value.Resolve(ctx);
        long playerNum = player == PlayerRef.Self ? ctx.PlayerNum : ctx.OpponentNum;
        long budget = ctx.State.GetBudget(playerNum);
        ctx.State.SetBudget(playerNum, budget - amount);
    }
}
