using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// Finds a matching card in the repository, removes it, creates a resource instance,
/// and enqueues a <see cref="AwaitingSlotSelect"/> entry for the player to choose a slot.
/// </summary>
public class RequestSlotFromRepoOp : IEffectOp
{
    /// <summary>Optional filter to restrict which cards can be deployed.</summary>
    public Func<CardDefinition, bool>? Filter { get; init; }

    /// <summary>Override AV for the deployed resource, or 0 to use default.</summary>
    public long OverrideAV { get; init; }

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        var repo = ctx.State.GetRepository(ctx.PlayerNum);

        var match = repo.FirstOrDefault(candidate =>
        {
            var definition = ctx.CardCache.Get(candidate.CardID);
            return definition is not null && (Filter is null || Filter(definition));
        });

        if (match is null) { return; }

        repo.Remove(match);

        var card = ctx.CardCache.MustGet(match.CardID);
        var instance = ResourceHelpers.CreateDeployedResource(
            card, ctx.State.NextInstanceID(), ctx.State.CurrentTurn, match.ArtNo);
        instance.DeployOrder = ctx.State.NextDeployOrder();

        if (OverrideAV > 0)
        {
            instance.MaxAV = OverrideAV;
            instance.Damage = 0;
        }

        var field = ctx.GetField(ctx.PlayerNum);
        var validZones = ResourceHelpers.BuildValidZones(field, card.CardType);

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

/// <summary>
/// Removes a card from hand by player choice, creates a resource instance,
/// and enqueues a <see cref="AwaitingSlotSelect"/> entry for the player to choose a slot.
/// </summary>
public class RequestSlotFromHandOp : IEffectOp
{
    /// <summary>Optional filter to restrict which cards can be deployed.</summary>
    public Func<CardDefinition, bool>? Filter { get; init; }

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        string? choiceCardId = ctx.ChoiceData?.GetValueOrDefault("cardId")?.ToString();

        if (choiceCardId is null)
        {
            throw new GameRuleException("No card chosen for deploy from hand");
        }

        if (Filter is not null)
        {
            var card = ctx.CardCache.MustGet(choiceCardId);
            if (!Filter(card))
            {
                throw new GameRuleException($"Card {choiceCardId} does not match filter");
            }
        }

        SlotRequestHelpers.DeployFromHand(ctx, choiceCardId);
    }
}

/// <summary>
/// Deploys the same card as the current target from the repository
/// by delegating to <see cref="RequestSlotFromRepoOp"/>.
/// </summary>
public class RequestSlotFromRepoSameCardOp(long overrideAV = 0) : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (ctx.Target is null)
        {
            throw new GameRuleException("No target for same card deploy");
        }

        var inner = new RequestSlotFromRepoOp
        {
            Filter = card => card.CardId == ctx.Target.CardID,
            OverrideAV = overrideAV,
        };
        inner.Execute(ctx);
    }
}
