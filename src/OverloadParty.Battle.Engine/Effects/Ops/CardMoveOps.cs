using System.Linq;
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
            var card = repo.First();
            repo.Remove(card);
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

        var found = repo.FirstOrDefault(card =>
        {
            var definition = ctx.CardCache.Get(card.CardID);
            if (definition is null) return false;
            if (Faction is { Length: > 0 } faction && definition.Faction != faction)
                return false;
            return true;
        });

        if (found is null) return;

        repo.Remove(found);
        hand.Add(new HandCard
        {
            InstanceID = ctx.State.NextInstanceID(),
            CardID = found.CardID,
        });
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
        if (!trash.Any()) return;

        // Get choice from ChoiceData
        var instanceId = ctx.ChoiceData?.GetValueOrDefault("instanceId")?.ToString();
        instanceId ??= trash.First().InstanceID;

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
