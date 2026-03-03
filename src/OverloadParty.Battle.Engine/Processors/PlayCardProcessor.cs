using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Processors;

public static class PlayCardProcessor
{
    public static ActionResult Process(
        GameState state, Game game, long playerNum,
        PlayCardRequest req, ICardCache cc, IEffectRegistry? effects)
    {
        var hand = state.GetHand(playerNum);
        var field = state.GetField(playerNum);

        // Find card in hand
        int handIdx = hand.FindIndex(h => h.InstanceID == req.CardInstanceID);
        if (handIdx < 0)
            throw new GameRuleException($"card {req.CardInstanceID} not in hand");

        var handCard = hand[handIdx];
        var cardDef = cc.MustGet(handCard.CardID);

        // Incident 1-per-turn limit
        if (cardDef.CardType == "Incident" && field.IncidentPlayedThisTurn)
            throw new GameRuleException("incident already played this turn");

        // Attachment flow
        if (cardDef.CardType == "Attachment")
            return ProcessAttachCard(state, game, playerNum, hand, handIdx, handCard, cardDef, req, cc, effects);

        // Validate position
        ValidatePlayPosition(cardDef, field, req);

        // Remove from hand
        hand.RemoveAt(handIdx);

        var deployOrder = state.NextDeployOrder();
        var events = new List<GameEvent>();

        if (FieldHelpers.IsSupportType(cardDef.CardType))
        {
            // Place in support zone
            var support = new SupportInstance
            {
                InstanceID = state.NextInstanceID(),
                CardID = cardDef.CardNo,
                FaceDown = cardDef.CardType == "Reactive",
                DeployingTurnsLeft = cardDef.DeployTurns,
                DeployOrder = deployOrder,
            };

            field.Support[req.Index] = support;

            // Immediate cards (Strategy, Incident): execute and remove
            if (FieldHelpers.IsImmediateType(cardDef.CardType))
            {
                if (cardDef.CardType == "Incident")
                    field.IncidentPlayedThisTurn = true;

                // Fire activate trigger
                if (effects?.Has(cardDef.CardNo, TriggerType.Activate) == true)
                {
                    var handler = effects.Get(cardDef.CardNo, TriggerType.Activate)!;
                    var ctx = new EffectContext
                    {
                        State = state,
                        Game = game,
                        PlayerNum = playerNum,
                        SupSource = support,
                        CardCache = cc,
                        ChoiceData = req.ChoiceData,
                    };
                    var effectResult = handler(ctx);
                    events.AddRange(effectResult.Events);
                }

                // Remove from support zone after execution
                field.Support[req.Index] = null;

                // Move to trash
                FieldHelpers.AddToTrash(state, playerNum, cardDef.CardNo, support.InstanceID);
            }
        }
        else
        {
            // Place resource on frontend or backend
            var resource = FieldHelpers.CreateResourceInstance(cardDef, state.NextInstanceID(), state.CurrentTurn);
            resource.DeployOrder = deployOrder;

            if (resource.FaceUp)
                field.HasHadActiveResource = true;

            if (req.Zone == "frontend")
                field.Frontend[req.Index] = resource;
            else
                field.Backend[req.Index] = resource;

            // Fire deploy reactives (opponent's TriggerOnEnemyDeploy)
            var (cancelled, reactiveEvents) = FireDeployReactives(
                state, game, playerNum, resource, cc, effects);
            events.AddRange(reactiveEvents);

            if (cancelled)
            {
                // Remove the deployed resource and send to trash
                FieldHelpers.RemoveResourceFromField(field, resource.InstanceID);
                FieldHelpers.AddToTrash(state, playerNum, cardDef.CardNo, resource.InstanceID);
            }
            else
            {
                // Fire deploy trigger on the resource itself
                if (effects?.Has(cardDef.CardNo, TriggerType.Deploy) == true)
                {
                    var handler = effects.Get(cardDef.CardNo, TriggerType.Deploy)!;
                    var ctx = new EffectContext
                    {
                        State = state,
                        Game = game,
                        PlayerNum = playerNum,
                        Source = resource,
                        CardCache = cc,
                        ChoiceData = req.ChoiceData,
                    };
                    var effectResult = handler(ctx);
                    events.AddRange(effectResult.Events);
                }
            }
        }

        var playerId = playerNum == 1 ? game.Player1ID : game.Player2ID;
        events.Insert(0, new GameEvent
        {
            GameID = game.GameID,
            EventType = "play_card",
            PlayerID = playerId,
            EventData = new Dictionary<string, object>
            {
                ["cardId"] = handCard.CardID,
                ["zone"] = req.Zone,
                ["index"] = req.Index,
            }
        });

        return new ActionResult { Events = events, StateUpdated = true };
    }

    private static ActionResult ProcessAttachCard(
        GameState state, Game game, long playerNum,
        List<HandCard> hand, int handIdx, HandCard handCard,
        CardDefinition cardDef, PlayCardRequest req,
        ICardCache cc, IEffectRegistry? effects)
    {
        if (req.TargetInstanceID is null)
            throw new GameRuleException("attachment requires target instance ID");

        var field = state.GetField(playerNum);
        var targetResult = FieldHelpers.FindResourceByID(field, req.TargetInstanceID);
        if (targetResult is null)
            throw new GameRuleException($"target resource {req.TargetInstanceID} not found");

        var target = targetResult.Value.Resource;
        if (target.Attachments.Count >= GameConstants.MaxAttachments)
            throw new GameRuleException($"target already has max attachments ({GameConstants.MaxAttachments})");

        // Add attachment
        var attachInstanceID = state.NextInstanceID();
        target.Attachments.Add(new AttachmentRef
        {
            InstanceID = attachInstanceID,
            CardID = cardDef.CardNo,
        });

        // Remove from hand
        hand.RemoveAt(handIdx);

        var events = new List<GameEvent>();

        // Fire deploy trigger for attachment
        if (effects?.Has(cardDef.CardNo, TriggerType.Deploy) == true)
        {
            var handler = effects.Get(cardDef.CardNo, TriggerType.Deploy)!;
            var ctx = new EffectContext
            {
                State = state,
                Game = game,
                PlayerNum = playerNum,
                Source = target,
                Target = target,
                CardCache = cc,
                ChoiceData = req.ChoiceData,
            };
            var effectResult = handler(ctx);
            events.AddRange(effectResult.Events);
        }

        var playerId = playerNum == 1 ? game.Player1ID : game.Player2ID;
        events.Insert(0, new GameEvent
        {
            GameID = game.GameID,
            EventType = "attach_card",
            PlayerID = playerId,
            EventData = new Dictionary<string, object>
            {
                ["cardId"] = handCard.CardID,
                ["targetId"] = req.TargetInstanceID,
            }
        });

        return new ActionResult { Events = events, StateUpdated = true };
    }

    private static void ValidatePlayPosition(CardDefinition cardDef, Field field, PlayCardRequest req)
    {
        if (req.Index < 0 || req.Index >= GameConstants.SlotsPerZone)
            throw new GameRuleException($"invalid slot index {req.Index}");

        switch (req.Zone)
        {
            case "frontend":
                if (!FieldHelpers.IsFrontendEligible(cardDef.CardType))
                    throw new GameRuleException($"{cardDef.CardType} cannot be placed in frontend");
                if (field.Frontend[req.Index] is not null)
                    throw new GameRuleException($"frontend slot {req.Index} is occupied");
                break;

            case "backend":
                if (!FieldHelpers.IsBackendEligible(cardDef.CardType))
                    throw new GameRuleException($"{cardDef.CardType} cannot be placed in backend");
                if (field.Backend[req.Index] is not null)
                    throw new GameRuleException($"backend slot {req.Index} is occupied");
                break;

            case "support":
                if (!FieldHelpers.IsSupportType(cardDef.CardType))
                    throw new GameRuleException($"{cardDef.CardType} cannot be placed in support");
                if (field.Support[req.Index] is not null)
                    throw new GameRuleException($"support slot {req.Index} is occupied");
                break;

            default:
                throw new GameRuleException($"unknown zone: {req.Zone}");
        }
    }

    private static (bool Cancelled, List<GameEvent> Events) FireDeployReactives(
        GameState state, Game game, long deployerNum,
        ResourceInstance deployed, ICardCache cc, IEffectRegistry? effects)
    {
        if (effects is null) return (false, []);

        var opponentNum = state.OpponentOf(deployerNum);
        var oppField = state.GetField(opponentNum);
        var allEvents = new List<GameEvent>();

        // Find all support cards with TriggerOnEnemyDeploy, sorted by DeployOrder
        var reactiveSupports = FieldHelpers.AllSupports(oppField)
            .Where(s => effects.Has(cc.MustGet(s.Support.CardID).CardNo, TriggerType.OnEnemyDeploy))
            .OrderBy(s => s.Support.DeployOrder)
            .ToList();

        foreach (var (support, idx) in reactiveSupports)
        {
            var supCard = cc.MustGet(support.CardID);
            var handler = effects.Get(supCard.CardNo, TriggerType.OnEnemyDeploy);
            if (handler is null) continue;

            var ctx = new EffectContext
            {
                State = state,
                Game = game,
                PlayerNum = opponentNum,
                SupSource = support,
                Target = deployed,
                CardCache = cc,
            };

            var result = handler(ctx);
            allEvents.AddRange(result.Events);

            if (result.CancelAction)
            {
                // Flip reactive face-up
                if (support.FaceDown)
                    support.FaceDown = false;
                return (true, allEvents);
            }
        }

        return (false, allEvents);
    }
}
