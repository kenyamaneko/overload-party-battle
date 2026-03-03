using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

public class DrawCardsOp(int count) : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        var repo = ctx.State.GetRepository(ctx.PlayerNum);
        var hand = ctx.State.GetHand(ctx.PlayerNum);

        int toDraw = Math.Min(count, repo.Count);
        for (int i = 0; i < toDraw; i++)
        {
            var card = repo[0];
            repo.RemoveAt(0);
            hand.Add(new HandCard
            {
                InstanceID = ctx.State.NextInstanceID(),
                CardID = card.CardID,
            });
        }
    }
}

public class SearchRepoOp : IEffectOp
{
    public string? Faction { get; init; }

    public void Execute(OpContext ctx)
    {
        var repo = ctx.State.GetRepository(ctx.PlayerNum);
        var hand = ctx.State.GetHand(ctx.PlayerNum);

        // Find first matching card in repository
        for (int i = 0; i < repo.Count; i++)
        {
            var card = ctx.CardCache.Get(repo[i].CardID);
            if (card is null) continue;

            if (Faction is { Length: > 0 } faction && card.Faction != faction)
                continue;

            var found = repo[i];
            repo.RemoveAt(i);
            hand.Add(new HandCard
            {
                InstanceID = ctx.State.NextInstanceID(),
                CardID = found.CardID,
            });
            return;
        }
    }
}

public class AddToHandOp(IAmountResolver cardNo) : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        long cardId = cardNo.Resolve(ctx);
        var hand = ctx.State.GetHand(ctx.PlayerNum);
        hand.Add(new HandCard
        {
            InstanceID = ctx.State.NextInstanceID(),
            CardID = cardId,
        });
    }
}

public class TrashToHandOp : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        var trash = ctx.State.GetTrash(ctx.PlayerNum);
        if (trash.Count == 0) return;

        // Get choice from ChoiceData
        var instanceId = ctx.ChoiceData?.GetValueOrDefault("instanceId")?.ToString();
        if (instanceId is null && trash.Count > 0)
        {
            // Default: take first
            instanceId = trash[0].InstanceID;
        }

        var idx = trash.FindIndex(c => c.InstanceID == instanceId);
        if (idx < 0) return;

        var card = trash[idx];
        trash.RemoveAt(idx);

        var hand = ctx.State.GetHand(ctx.PlayerNum);
        hand.Add(new HandCard
        {
            InstanceID = ctx.State.NextInstanceID(),
            CardID = card.CardID,
        });
    }
}
