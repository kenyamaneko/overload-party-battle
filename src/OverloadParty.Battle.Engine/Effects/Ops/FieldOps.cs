using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// Checks for and destroys any resources whose effective AV has reached zero or below,
/// following the turn-player-first destruction order.
/// </summary>
public class DestroyCheckOp : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        foreach (var evt in DestructionSweep.Run(ctx.State, ctx.Game, ctx.CardCache, ctx.Effects))
        {
            ctx.AddEvent(evt);
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
/// 相手のサポートゾーンにある裏向きリアクティブカードから、確認する 1 枚を決める。
/// </summary>
internal static class FaceDownReactiveTarget
{
    /// <summary>選択値を ChoiceData で受け渡す key。</summary>
    private const string ChoiceKey = "instanceId";

    /// <summary>
    /// 確認対象を決めます。候補が複数あって選択値が無ければ選択待ちに入ります。
    /// </summary>
    /// <param name="ctx">効果実行コンテキスト。</param>
    /// <returns>確認対象のカード。候補が無い場合と選択待ちに入った場合は null。</returns>
    public static DeployedSupport? Resolve(OpContext ctx)
    {
        var faceDown = ctx.OpponentField.Support.Where(s => !s.FaceUp).ToList();
        if (faceDown.Count == 0) { return null; }
        if (faceDown.Count == 1) { return faceDown[0]; }

        if (ctx.ChoiceData?.GetValueOrDefault(ChoiceKey)?.ToString() is not { } chosenId)
        {
            ctx.SuspendForChoice(
                ChoiceKey, ChoiceKinds.FaceDownReactive,
                faceDown.Select(s => s.InstanceID).ToList(), ctx.PlayerNum);
            return null;
        }

        return faceDown.FirstOrDefault(s => s.InstanceID == chosenId)
            ?? throw new GameRuleException($"Face-down reactive {chosenId} not found on the opponent's field");
    }
}

/// <summary>
/// Reveals a face-down reactive card chosen from the opponent's support zone.
/// </summary>
public class RevealReactiveOp : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (FaceDownReactiveTarget.Resolve(ctx) is not { } target) { return; }

        target.FaceUp = true;
    }
}

/// <summary>
/// Peeks at a face-down reactive card chosen from the opponent's support zone.
/// The card stays face-down but becomes visible to the activating player.
/// </summary>
public class PeekReactiveOp : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (FaceDownReactiveTarget.Resolve(ctx) is not { } target) { return; }

        if (!target.PeekedBy.Contains(ctx.PlayerNum))
        {
            target.PeekedBy.Add(ctx.PlayerNum);
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

        FieldHelpers.DestroySupport(ctx.State, ctx.Game, ctx.OpponentNum, oppField, target.InstanceID, ctx.CardCache, ctx.Effects);
    }
}

/// <summary>
/// 発火元リソースの残デプロイターンを縮め、0 になったらその場で稼働させる。
/// </summary>
public class ReduceDeployTurnsOp(IAmountResolver value) : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (ctx.Source is null) { return; }

        // 既に稼働しているリソースには短縮する残ターンがない。
        if (ctx.Source.DeployingTurnsLeft <= 0) { return; }

        long amount = value.Resolve(ctx);
        ctx.Source.DeployingTurnsLeft = Math.Max(0, ctx.Source.DeployingTurnsLeft - amount);

        if (ctx.Source.DeployingTurnsLeft > 0) { return; }

        ctx.Source.FaceUp = true;

        var (_, events) = DeployCompletion.CompleteResource(
            ctx.State, ctx.Game, ctx.PlayerNum, ctx.Source, ctx.CardCache, ctx.Effects);
        foreach (var evt in events)
        {
            ctx.AddEvent(evt);
        }
    }
}
