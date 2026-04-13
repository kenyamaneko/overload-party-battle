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
    public static void DeployFromHand(OpContext ctx, string choiceCardId)
    {
        var card = ctx.CardCache.MustGet(choiceCardId);

        var hand = ctx.State.GetHand(ctx.PlayerNum);
        int handIdx = hand.FindIndex(c => c.CardID == choiceCardId);
        if (handIdx < 0)
        {
            throw new GameRuleException($"Card {choiceCardId} not in hand");
        }

        var handCard = hand[handIdx];
        hand.RemoveAt(handIdx);

        var instance = ResourceHelpers.CreateDeployedResource(
            card, ctx.State.NextInstanceID(), ctx.State.CurrentTurn, handCard.ArtNo);
        instance.DeployOrder = ctx.State.NextDeployOrder();

        var field = ctx.GetField(ctx.PlayerNum);
        var validZones = ResourceHelpers.BuildValidZones(field, card);

        if (validZones.Count == 0)
        {
            throw new GameRuleException("No empty slot for effect deploy");
        }

        ctx.State.PendingSlotSelects.Add(new AwaitingSlotSelect
        {
            PlayerNum = ctx.PlayerNum,
            Resource = instance,
            ValidZones = validZones,
        });
    }
}
