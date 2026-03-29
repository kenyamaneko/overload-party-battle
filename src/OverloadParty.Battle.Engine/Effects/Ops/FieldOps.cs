using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// Checks for and destroys any resources whose effective AV has reached zero or below.
/// </summary>
public class DestroyCheckOp(PlayerRef player) : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (player == PlayerRef.Both)
        {
            DestroyZeroed(ctx, ctx.PlayerNum);
            DestroyZeroed(ctx, ctx.OpponentNum);
            return;
        }

        long playerNum = player == PlayerRef.Self ? ctx.PlayerNum : ctx.OpponentNum;
        DestroyZeroed(ctx, playerNum);
    }

    private static void DestroyZeroed(OpContext ctx, long playerNum)
    {
        var field = ctx.GetField(playerNum);
        var toDestroy = FieldHelpers.AllResources(field)
            .Where(res => res.EffectiveAV <= 0)
            .ToList();

        foreach (var res in toDestroy)
        {
            ResourceHelpers.DestroyResource(ctx.State, playerNum, field, res, ctx.CardCache);
        }
    }
}

/// <summary>
/// Scales the source resource to the specified rank.
/// </summary>
public class ScaleToRankOp(string rank) : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (ctx.Source is null) { return; }

        var targetRank = EnumExtensions.ParseRank(rank);
        ResourceHelpers.ChangeRank(ctx.Source, targetRank, ctx.MyField, ctx.CardCache);
    }
}

/// <summary>
/// Reveals the first hidden reactive card in the opponent's support zone.
/// </summary>
public class RevealReactiveOp : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        var oppField = ctx.OpponentField;
        var hidden = oppField.Support.FirstOrDefault(s => !s.FaceUp);
        hidden?.FaceUp = true;
    }
}

/// <summary>
/// Peeks at the first hidden reactive card in the opponent's support zone.
/// The card stays face-down but becomes visible to the activating player.
/// </summary>
public class PeekReactiveOp : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        var oppField = ctx.OpponentField;
        var hidden = oppField.Support.FirstOrDefault(s => !s.FaceUp);
        if (hidden is null) { return; }

        if (!hidden.PeekedBy.Contains(ctx.PlayerNum))
        {
            hidden.PeekedBy.Add(ctx.PlayerNum);
        }
    }
}

/// <summary>
/// Destroys a platform card in the opponent's support zone.
/// </summary>
public class DestroyPlatformOp : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        var oppField = ctx.OpponentField;

        var instanceId = ctx.ChoiceData?.GetValueOrDefault("instanceId")?.ToString();

        var target = instanceId is not null
            ? oppField.Support.FirstOrDefault(s => s.InstanceID == instanceId)
                ?? throw new GameRuleException($"Support {instanceId} not found on opponent field")
            : oppField.Support.FirstOrDefault(s => ctx.CardCache.Get(s.CardID)?.CardType == CardTypes.Platform);

        if (target is null) { return; }

        var targetCard = ctx.CardCache.Get(target.CardID);
        if (targetCard?.CardType != CardTypes.Platform)
        {
            throw new GameRuleException($"Selected support {instanceId} is not a Platform");
        }

        FieldHelpers.DestroySupport(ctx.State, ctx.OpponentNum, oppField, target.InstanceID);
    }
}

/// <summary>
/// Reduces the source resource's remaining deploy turns.
/// </summary>
public class ReduceDeployTurnsOp(IAmountResolver value) : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (ctx.Source is null) { return; }

        long amount = value.Resolve(ctx);
        ctx.Source.DeployingTurnsLeft = Math.Max(0, ctx.Source.DeployingTurnsLeft - amount);
    }
}
