using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// DrawCardsOp はプレイヤーのリポジトリからカードを手札に引きます
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
/// SearchRepoOp はプレイヤーのリポジトリを検索し条件に合うカードを手札に加えます
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
/// Requires <see cref="OpContext.Target"/> to be non-null.
/// </summary>
public class AddToHandOp : IEffectOp
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly AddToHandOp Instance = new();

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx.Target);
        CardMoveHelpers.AddToHand(ctx.State, ctx.PlayerNum, ctx.Target.CardID);
    }
}

/// <summary>
/// Returns a player-chosen card from trash to hand.
/// The choice is always required; the op never auto-picks.
/// A <see cref="Filter"/> may restrict which trash cards qualify.
/// </summary>
public class TrashToHandOp : IEffectOp
{
    /// <summary>Optional filter restricting which trash cards can be returned.</summary>
    public Func<CardDefinition, bool>? Filter { get; init; }

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        var instanceId = ctx.ChoiceData?.GetValueOrDefault("instanceId")?.ToString()
            ?? throw new GameRuleException("No card chosen for trash_to_hand");

        var trash = ctx.State.GetTrash(ctx.PlayerNum);
        var chosen = trash.FirstOrDefault(c => c.InstanceID == instanceId)
            ?? throw new GameRuleException($"chosen instance {instanceId} not in trash");

        if (Filter is not null)
        {
            var card = ctx.CardCache.MustGet(chosen.CardID);
            if (!Filter(card))
            {
                throw new GameRuleException($"Card {chosen.CardID} does not match trash_to_hand filter");
            }
        }

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

