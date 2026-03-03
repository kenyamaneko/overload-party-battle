using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Processors;

public static class ActivateEffectProcessor
{
    public static ActionResult Process(
        GameState state, Game game, long playerNum,
        ActivateEffectRequest req, ICardCache cc, IEffectRegistry? effects)
    {
        if (effects is null)
            throw new GameRuleException("effect system not initialized");

        var field = state.GetField(playerNum);

        // Try to find as resource first
        var resourceResult = FieldHelpers.FindResourceByID(field, req.InstanceID);
        if (resourceResult is not null)
        {
            return ActivateResourceEffect(state, game, playerNum, resourceResult.Value, req, cc, effects);
        }

        // Try support zone
        var supportResult = FieldHelpers.FindSupportByID(field, req.InstanceID);
        if (supportResult is not null)
        {
            return ActivateSupportEffect(state, game, playerNum, field, supportResult.Value, req, cc, effects);
        }

        throw new GameRuleException($"resource {req.InstanceID} not found on field");
    }

    private static ActionResult ActivateResourceEffect(
        GameState state, Game game, long playerNum,
        (ResourceInstance Resource, Zone Zone, int Index) found,
        ActivateEffectRequest req, ICardCache cc, IEffectRegistry effects)
    {
        var (source, _, _) = found;
        var card = cc.MustGet(source.CardID);

        if (!effects.Has(card.CardNo, TriggerType.Activate))
            throw new GameRuleException($"card {card.CardNo} has no activate effect");

        if (source.EffectUsedThisTurn)
            throw new GameRuleException("effect already used this turn");

        if (FieldHelpers.HasTemporaryEffect(source, "cannot_operate"))
            throw new GameRuleException("resource cannot operate");

        // Find target if specified
        ResourceInstance? target = null;
        if (req.TargetInstanceID is { } targetId)
        {
            // Search own field first
            var ownResult = FieldHelpers.FindResourceByID(state.GetField(playerNum), targetId);
            if (ownResult is not null)
            {
                target = ownResult.Value.Resource;
            }
            else
            {
                // Search opponent field
                var oppResult = FieldHelpers.FindResourceByID(
                    state.GetField(state.OpponentOf(playerNum)), targetId);
                if (oppResult is not null)
                    target = oppResult.Value.Resource;
            }
        }

        var handler = effects.Get(card.CardNo, TriggerType.Activate)!;
        var ctx = new EffectContext
        {
            State = state,
            Game = game,
            PlayerNum = playerNum,
            Source = source,
            Target = target,
            CardCache = cc,
            ChoiceData = req.ChoiceData,
        };

        var result = handler(ctx);
        source.EffectUsedThisTurn = true;

        var playerId = playerNum == 1 ? game.Player1ID : game.Player2ID;
        var events = new List<GameEvent>(result.Events);
        events.Insert(0, new GameEvent
        {
            GameID = game.GameID,
            EventType = "activate_effect",
            PlayerID = playerId,
            EventData = new Dictionary<string, object>
            {
                ["cardNo"] = card.CardNo,
                ["sourceId"] = req.InstanceID,
            }
        });
        if (req.TargetInstanceID is not null)
            events[0].EventData!["targetId"] = req.TargetInstanceID;

        return new ActionResult { Events = events, StateUpdated = true };
    }

    private static ActionResult ActivateSupportEffect(
        GameState state, Game game, long playerNum,
        Field field, (SupportInstance Support, int Index) found,
        ActivateEffectRequest req, ICardCache cc, IEffectRegistry effects)
    {
        var (support, _) = found;
        var card = cc.MustGet(support.CardID);

        if (!effects.Has(card.CardNo, TriggerType.Activate))
            throw new GameRuleException($"support card {card.CardNo} has no activate effect");

        var handler = effects.Get(card.CardNo, TriggerType.Activate)!;
        var ctx = new EffectContext
        {
            State = state,
            Game = game,
            PlayerNum = playerNum,
            SupSource = support,
            CardCache = cc,
            ChoiceData = req.ChoiceData,
        };

        var result = handler(ctx);

        var playerId = playerNum == 1 ? game.Player1ID : game.Player2ID;
        var events = new List<GameEvent>(result.Events);
        events.Insert(0, new GameEvent
        {
            GameID = game.GameID,
            EventType = "activate_effect",
            PlayerID = playerId,
            EventData = new Dictionary<string, object>
            {
                ["cardNo"] = card.CardNo,
                ["sourceId"] = req.InstanceID,
            }
        });

        return new ActionResult { Events = events, StateUpdated = true };
    }
}
