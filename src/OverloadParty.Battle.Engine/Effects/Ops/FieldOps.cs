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
        long playerNum = player == PlayerRef.Self ? ctx.PlayerNum : ctx.OpponentNum;
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
        ResourceHelpers.ChangeRank(ctx.Source, targetRank, ctx.CardCache);
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
