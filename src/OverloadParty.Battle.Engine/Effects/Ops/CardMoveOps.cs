using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

public class DrawCardsOp(int count) : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        CardMoveHelpers.DrawCards(ctx.State, ctx.PlayerNum, count);
    }
}

public class SearchRepoOp : IEffectOp
{
    public string? Faction { get; init; }

    public void Execute(OpContext ctx)
    {
        CardMoveHelpers.SearchRepo(ctx.State, ctx.PlayerNum, card =>
        {
            if (Faction is not { Length: > 0 } faction)
            {
                return true;
            }
            var definition = ctx.CardCache.Get(card.CardID);
            return definition?.Faction == faction;
        });
    }
}

public class AddToHandOp(IAmountResolver cardNo) : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        CardMoveHelpers.AddToHand(ctx.State, ctx.PlayerNum, cardNo.Resolve(ctx));
    }
}

public class TrashToHandOp : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        var trash = ctx.State.GetTrash(ctx.PlayerNum);
        if (trash.Count == 0)
        {
            return;
        }

        var instanceId = ctx.ChoiceData?.GetValueOrDefault("instanceId")?.ToString()
            ?? trash[0].InstanceID;

        CardMoveHelpers.TrashToHand(ctx.State, ctx.PlayerNum, instanceId);
    }
}

/// <summary>
/// Deploys the first matching card from the repository to an empty field slot.
/// </summary>
public class DeployFromRepoOp : IEffectOp
{
    public Func<CardDefinition, bool>? Filter { get; init; }
    public long OverrideAV { get; init; }

    public void Execute(OpContext ctx)
    {
        var repo = ctx.State.GetRepository(ctx.PlayerNum);

        var match = repo.FirstOrDefault(candidate =>
        {
            var definition = ctx.CardCache.Get(candidate.CardID);
            return definition is not null && (Filter is null || Filter(definition));
        });

        if (match is null) { return; }

        var field = ctx.GetField(ctx.PlayerNum);
        ResourceHelpers.DeployFromRepo(ctx.State, ctx.PlayerNum, field, match, OverrideAV, ctx.CardCache);
    }
}

/// <summary>
/// Deploys a card from hand by player choice.
/// </summary>
public class DeployFromHandOp : IEffectOp
{
    public Func<CardDefinition, bool>? Filter { get; init; }

    public void Execute(OpContext ctx)
    {
        long? choiceCardNo = null;
        if (ctx.ChoiceData?.TryGetValue("cardNo", out var val) == true)
        {
            if (val is long l)
            {
                choiceCardNo = l;
            }
            else if (val is int i)
            {
                choiceCardNo = i;
            }
            else if (long.TryParse(val?.ToString(), out var parsed))
            {
                choiceCardNo = parsed;
            }
        }

        if (choiceCardNo is null)
        {
            throw new GameRuleException("No card chosen for deploy from hand");
        }

        if (Filter is not null)
        {
            var card = ctx.CardCache.Get(choiceCardNo.Value);
            if (card is null || !Filter(card))
            {
                throw new GameRuleException($"Card {choiceCardNo} does not match filter");
            }
        }

        var field = ctx.GetField(ctx.PlayerNum);
        ResourceHelpers.DeployFromHand(ctx.State, ctx.PlayerNum, field, choiceCardNo.Value, ctx.CardCache);
    }
}

/// <summary>
/// Deploys the same card as the current target from the repository.
/// </summary>
public class DeployFromRepoSameCardOp(long overrideAV = 0) : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        if (ctx.Target is null)
        {
            throw new GameRuleException("No target for same card deploy");
        }

        var inner = new DeployFromRepoOp
        {
            Filter = card => card.CardNo == ctx.Target.CardID,
            OverrideAV = overrideAV,
        };
        inner.Execute(ctx);
    }
}
