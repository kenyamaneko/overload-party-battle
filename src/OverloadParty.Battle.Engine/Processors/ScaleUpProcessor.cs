using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// Processes scale-up actions that change a resource's rank or instance family.
/// </summary>
public static class ScaleUpProcessor
{
    /// <summary>
    /// Changes a resource's rank and optionally its instance family.
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="game">The game metadata.</param>
    /// <param name="playerNum">The player number performing the action.</param>
    /// <param name="req">The scale-up request containing target rank and optional instance family.</param>
    /// <param name="cc">The card definition cache.</param>
    /// <returns>The action result containing the scale-up event and state update flag.</returns>
    public static ActionResult Process(
        BattleGameState state, Game game, long playerNum,
        ScaleUpRequest req, ICardCache cc, IEffectRegistry? effects = null)
    {
        var field = state.GetField(playerNum);

        var resource = FieldHelpers.FindResourceByID(field, req.InstanceID)
            ?? throw new GameRuleException($"resource {req.InstanceID} not found");
        var card = cc.MustGet(resource.CardID);

        if (!card.Resizable)
        {
            throw new GameRuleException("card is not resizable");
        }

        var targetRank = EnumExtensions.ParseRank(req.TargetRank);

        if (resource.Rank is null || targetRank <= resource.Rank)
        {
            throw new GameRuleException("can only scale up to a higher rank");
        }

        var targetFamily = req.InstanceFamily is not null
            ? EnumExtensions.ParseInstanceFamily(req.InstanceFamily)
            : resource.InstanceFamily;

        if (targetFamily is null)
        {
            throw new GameRuleException("instance family required for medium or large rank");
        }

        if (resource.Rank == Rank.Medium && targetFamily != resource.InstanceFamily)
        {
            throw new GameRuleException("cannot change instance family when scaling up from medium");
        }
        resource.InstanceFamily = targetFamily;

        ResourceHelpers.ChangeRank(resource, targetRank, field, cc);

        // Fire OnScaleUp triggers (resource itself + attachments)
        var events = new List<GameEvent>();
        if (effects is not null)
        {
            FireOnScaleUp(state, game, playerNum, resource, cc, effects, events);
        }

        var evt = new GameEvent
        {
            GameID = game.GameID,
            EventType = ActionTypes.ScaleUp,
            PlayerNum = playerNum,
            EventData = new ScaleUpEventData
            {
                InstanceId = req.InstanceID,
                TargetRank = req.TargetRank,
                InstanceFamily = req.InstanceFamily,
            },
        };

        events.Insert(0, evt);
        return new ActionResult { Events = events, StateUpdated = true };
    }

    private static void FireOnScaleUp(
        BattleGameState state, Game game, long playerNum,
        DeployedResource resource, ICardCache cc, IEffectRegistry effects,
        List<GameEvent> events)
    {
        var candidates = new List<(string CardId, DeployedResource Source)>();

        if (effects.Has(resource.CardID, TriggerType.OnScaleUp))
        {
            candidates.Add((resource.CardID, resource));
        }

        var field = state.GetField(playerNum);
        foreach (var att in field.Support.Where(a => a.TargetInstanceID == resource.InstanceID))
        {
            if (effects.Has(att.CardID, TriggerType.OnScaleUp))
            {
                candidates.Add((att.CardID, resource));
            }
        }

        foreach (var (cardId, source) in candidates)
        {
            var handler = effects.Get(cardId, TriggerType.OnScaleUp);
            if (handler is null) { continue; }

            var result = handler(new EffectContext
            {
                State = state,
                Game = game,
                PlayerNum = playerNum,
                Source = source,
                CardCache = cc,
            });
            if (!result.GuardFailed) { events.AddRange(result.Events); }
        }
    }
}
