using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// Shared logic for removing a card from hand and queueing it for slot selection.
/// </summary>
public static class SlotRequestHelpers
{
    /// <summary>
    /// Removes the chosen card from hand, creates a <see cref="DeployedResource"/>,
    /// computes valid zones, and enqueues a <see cref="AwaitingSlotSelect"/> entry.
    /// </summary>
    /// <param name="ctx">パイプライン実行コンテキスト。</param>
    /// <param name="choiceCardId">手札からデプロイするカードの ID。</param>
    public static void DeployFromHand(OpContext ctx, string choiceCardId)
    {
        var card = ctx.CardCache.MustGet(choiceCardId);

        var hand = ctx.State.GetHand(ctx.PlayerNum);
        int handIdx = hand.FindIndex(c => c.CardID == choiceCardId);
        if (handIdx < 0)
        {
            throw new GameRuleException($"Card {choiceCardId} not in hand");
        }

        // 配置先がないなら手札から取り除く前に GuardFailed で抜ける。
        // 取り除いてから判定すると、解決失敗時に手札からカードが消えたまま戻せなくなる。
        var field = ctx.GetField(ctx.PlayerNum);
        var validZones = ResourceHelpers.BuildValidZones(field, card);
        if (validZones.Count == 0)
        {
            ctx.Result.GuardFailed = true;
            return;
        }

        var handCard = hand[handIdx];
        hand.RemoveAt(handIdx);

        var instance = ResourceHelpers.CreateDeployedResource(
            card, ctx.State.NextInstanceID(), ctx.State.CurrentTurn, handCard.ArtNo);
        instance.DeployOrder = ctx.State.NextDeployOrder();

        ctx.State.PendingSlotSelects.Add(new AwaitingSlotSelect
        {
            PlayerNum = ctx.PlayerNum,
            Resource = instance,
            ValidZones = validZones,
        });
    }
}
