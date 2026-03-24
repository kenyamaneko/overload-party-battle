using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// Draws cards from the player's deck into their hand.
/// </summary>
public class DrawCardsOp(int count) : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        CardMoveHelpers.DrawCards(ctx.State, ctx.PlayerNum, count);
    }
}

/// <summary>
/// Searches the player's repository and adds a matching card to hand.
/// </summary>
public class SearchRepoOp : IEffectOp
{
    /// <summary>Faction filter, or null for any faction.</summary>
    public string? Faction { get; init; }

    /// <inheritdoc />
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

/// <summary>
/// Adds the target resource's card to the player's hand.
/// </summary>
public class AddToHandOp : IEffectOp
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly AddToHandOp Instance = new();

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        CardMoveHelpers.AddToHand(ctx.State, ctx.PlayerNum, ctx.Target!.CardID);
    }
}

/// <summary>
/// Returns a card from the player's trash to their hand.
/// </summary>
public class TrashToHandOp : IEffectOp
{
    /// <inheritdoc />
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

        var field = ctx.GetField(ctx.PlayerNum);
        ResourceHelpers.DeployFromRepo(ctx.State, ctx.PlayerNum, field, match, OverrideAV, ctx.CardCache);
    }
}

/// <summary>
/// Deploys a card from hand by player choice.
/// </summary>
public class DeployFromHandOp : IEffectOp
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
            var card = ctx.CardCache.Get(choiceCardId);
            if (card is null || !Filter(card))
            {
                throw new GameRuleException($"Card {choiceCardId} does not match filter");
            }
        }

        var field = ctx.GetField(ctx.PlayerNum);
        ResourceHelpers.DeployFromHand(ctx.State, ctx.PlayerNum, field, choiceCardId, ctx.CardCache);
    }
}

/// <summary>
/// Deploys the same card as the current target from the repository.
/// </summary>
public class DeployFromRepoSameCardOp(long overrideAV = 0) : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (ctx.Target is null)
        {
            throw new GameRuleException("No target for same card deploy");
        }

        var inner = new DeployFromRepoOp
        {
            Filter = card => card.CardId == ctx.Target.CardID,
            OverrideAV = overrideAV,
        };
        inner.Execute(ctx);
    }
}
