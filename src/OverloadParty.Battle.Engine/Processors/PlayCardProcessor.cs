using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// Processes play-card actions, placing resources, supports, and attachments onto the field.
/// </summary>
public static class PlayCardProcessor
{
    private record PlayContext(
        GameState State, Game Game, long PlayerNum,
        ICardCache CC, IEffectRegistry? Effects);

    /// <summary>
    /// Plays a card from the player's hand onto the field, handling resource, support, and attachment placement.
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="game">The game metadata.</param>
    /// <param name="playerNum">The player number performing the action.</param>
    /// <param name="req">The play card request containing card and placement details.</param>
    /// <param name="cc">The card definition cache.</param>
    /// <param name="effects">The optional effect registry for triggering deploy effects.</param>
    /// <returns>The action result containing generated events and state update flag.</returns>
    public static ActionResult Process(
        GameState state, Game game, long playerNum,
        PlayCardRequest req, ICardCache cc, IEffectRegistry? effects)
    {
        var hand = state.GetHand(playerNum);
        var field = state.GetField(playerNum);

        int handIdx = hand.FindIndex(h => h.InstanceID == req.CardInstanceID);
        if (handIdx < 0)
        {
            throw new GameRuleException($"card {req.CardInstanceID} not in hand");
        }

        var handCard = hand[handIdx];
        var cardDef = cc.MustGet(handCard.CardID);

        if (cardDef.CardType == CardTypes.Incident && state.GetIncidentPlayedThisTurn(playerNum))
        {
            throw new GameRuleException("incident already played this turn");
        }

        var ctx = new PlayContext(state, game, playerNum, cc, effects);

        if (cardDef.CardType == CardTypes.Attachment)
        {
            return ProcessAttachCard(ctx, hand, handIdx, handCard, cardDef, req);
        }

        if (FieldHelpers.IsImmediateType(cardDef.CardType))
        {
            return ProcessImmediateCard(ctx, hand, handIdx, handCard, cardDef, req);
        }

        ValidatePlayPosition(cardDef, field, req);

        hand.RemoveAt(handIdx);

        var deployOrder = state.NextDeployOrder();
        var events = new List<GameEvent>();

        bool cancelled = false;
        if (FieldHelpers.IsSupportType(cardDef.CardType))
        {
            PlaceSupport(ctx, field, cardDef, handCard, req, deployOrder, events);
        }
        else
        {
            cancelled = PlaceResource(ctx, field, cardDef, handCard, req, deployOrder, events);
        }

        var playerId = game.GetPlayerID(playerNum);
        events.Insert(0, new GameEvent
        {
            GameID = game.GameID,
            EventType = WireActionTypes.PlayCard,
            PlayerID = playerId,
            EventData = new PlayCardEventData
            {
                CardId = handCard.CardID,
                Zone = req.Zone,
                Index = req.Index,
                Cancelled = cancelled ? true : null,
            }.ToDictionary(),
        });

        return new ActionResult { Events = events, StateUpdated = true };
    }

    /// <summary>
    /// Strategy/Incident: サポートゾーンを使わず、手札から直接発動してトラッシュへ送る。
    /// </summary>
    private static ActionResult ProcessImmediateCard(
        PlayContext ctx,
        List<UndeployedCard> hand, int handIdx, UndeployedCard handCard,
        CardDefinition cardDef, PlayCardRequest req)
    {
        hand.RemoveAt(handIdx);

        var instanceID = ctx.State.NextInstanceID();

        if (cardDef.CardType == CardTypes.Incident)
        {
            ctx.State.SetIncidentPlayedThisTurn(ctx.PlayerNum, true);
        }

        var events = new List<GameEvent>();

        if (ctx.Effects?.Has(cardDef.CardId, TriggerType.Activate) == true)
        {
            var handler = ctx.Effects.Get(cardDef.CardId, TriggerType.Activate)!;
            var effectCtx = new EffectContext
            {
                State = ctx.State,
                Game = ctx.Game,
                PlayerNum = ctx.PlayerNum,
                CardCache = ctx.CC,
                ChoiceData = req.ChoiceData,
            };
            var effectResult = handler(effectCtx);
            events.AddRange(effectResult.Events);
        }

        CardMoveHelpers.AddToTrash(ctx.State, ctx.PlayerNum, cardDef.CardId, instanceID, handCard.ArtNo);

        var playerId = ctx.Game.GetPlayerID(ctx.PlayerNum);
        events.Insert(0, new GameEvent
        {
            GameID = ctx.Game.GameID,
            EventType = WireActionTypes.PlayCard,
            PlayerID = playerId,
            EventData = new PlayCardEventData
            {
                CardId = handCard.CardID,
                Zone = "",
                Index = -1,
            }.ToDictionary(),
        });

        return new ActionResult { Events = events, StateUpdated = true };
    }

    private static void PlaceSupport(
        PlayContext ctx, Field field,
        CardDefinition cardDef, UndeployedCard handCard, PlayCardRequest req,
        long deployOrder, List<GameEvent> events)
    {
        var support = new DeployedSupport
        {
            InstanceID = ctx.State.NextInstanceID(),
            CardID = cardDef.CardId,
            ArtNo = handCard.ArtNo,
            FaceUp = cardDef.CardType != CardTypes.Reactive,
            DeployingTurnsLeft = cardDef.DeployTurns,
            DeployOrder = deployOrder,
        };

        field.Support[req.Index] = support;

        if (support.DeployingTurnsLeft <= 0
            && ctx.Effects?.Has(cardDef.CardId, TriggerType.Deploy) == true)
        {
            var handler = ctx.Effects.Get(cardDef.CardId, TriggerType.Deploy)!;
            var effectCtx = new EffectContext
            {
                State = ctx.State,
                Game = ctx.Game,
                PlayerNum = ctx.PlayerNum,
                SupSource = support,
                CardCache = ctx.CC,
            };
            var effectResult = handler(effectCtx);
            events.AddRange(effectResult.Events);
        }

        FieldChangeTrigger.Fire(ctx.State, ctx.Game, ctx.CC, ctx.Effects);
    }

    private static bool PlaceResource(
        PlayContext ctx, Field field,
        CardDefinition cardDef, UndeployedCard handCard, PlayCardRequest req,
        long deployOrder, List<GameEvent> events)
    {
        var resource = ResourceHelpers.CreateDeployedResource(cardDef, ctx.State.NextInstanceID(), ctx.State.CurrentTurn, handCard.ArtNo);
        resource.DeployOrder = deployOrder;

        if (resource.FaceUp)
        {
            ctx.State.SetHasHadActiveResource(ctx.PlayerNum, true);
        }

        if (req.Zone == GameConstants.ZoneFrontend)
        {
            field.Frontend[req.Index] = resource;
        }
        else
        {
            field.Backend[req.Index] = resource;
        }

        // Fire deploy reactives (opponent's TriggerOnEnemyDeploy)
        var (cancelled, reactiveEvents) = FireDeployReactives(ctx, resource);
        events.AddRange(reactiveEvents);

        if (cancelled)
        {
            // Remove the deployed resource and send to trash
            FieldHelpers.RemoveResourceFromField(field, resource.InstanceID);
            CardMoveHelpers.AddToTrash(ctx.State, ctx.PlayerNum, cardDef.CardId, resource.InstanceID, resource.ArtNo);
            return true;
        }

        // Fire deploy trigger on the resource itself
        if (ctx.Effects?.Has(cardDef.CardId, TriggerType.Deploy) == true)
        {
            var handler = ctx.Effects.Get(cardDef.CardId, TriggerType.Deploy)!;
            var effectCtx = new EffectContext
            {
                State = ctx.State,
                Game = ctx.Game,
                PlayerNum = ctx.PlayerNum,
                Source = resource,
                CardCache = ctx.CC,
                ChoiceData = req.ChoiceData,
            };
            var effectResult = handler(effectCtx);
            events.AddRange(effectResult.Events);
        }

        FieldChangeTrigger.Fire(ctx.State, ctx.Game, ctx.CC, ctx.Effects);

        return false;
    }

    private static ActionResult ProcessAttachCard(
        PlayContext ctx,
        List<UndeployedCard> hand, int handIdx, UndeployedCard handCard,
        CardDefinition cardDef, PlayCardRequest req)
    {
        if (req.TargetInstanceID is null)
        {
            throw new GameRuleException("attachment requires target instance ID");
        }

        var field = ctx.State.GetField(ctx.PlayerNum);
        var target = FieldHelpers.FindResourceByID(field, req.TargetInstanceID)
            ?? throw new GameRuleException($"target resource {req.TargetInstanceID} not found");
        if (target.Attachments.Count >= BattleConstants.MaxAttachments)
        {
            throw new GameRuleException($"target already has max attachments ({BattleConstants.MaxAttachments})");
        }

        var attachInstanceID = ctx.State.NextInstanceID();
        target.Attachments.Add(new AttachmentRef
        {
            InstanceID = attachInstanceID,
            CardID = cardDef.CardId,
            ArtNo = handCard.ArtNo,
        });

        hand.RemoveAt(handIdx);

        var events = new List<GameEvent>();

        // Fire deploy trigger for attachment
        if (ctx.Effects?.Has(cardDef.CardId, TriggerType.Deploy) == true)
        {
            var handler = ctx.Effects.Get(cardDef.CardId, TriggerType.Deploy)!;
            var effectCtx = new EffectContext
            {
                State = ctx.State,
                Game = ctx.Game,
                PlayerNum = ctx.PlayerNum,
                Source = target,
                Target = target,
                CardCache = ctx.CC,
                ChoiceData = req.ChoiceData,
            };
            var effectResult = handler(effectCtx);
            events.AddRange(effectResult.Events);
        }

        FieldChangeTrigger.Fire(ctx.State, ctx.Game, ctx.CC, ctx.Effects);

        var playerId = ctx.Game.GetPlayerID(ctx.PlayerNum);
        events.Insert(0, new GameEvent
        {
            GameID = ctx.Game.GameID,
            EventType = WireActionTypes.AttachCard,
            PlayerID = playerId,
            EventData = new AttachCardEventData
            {
                CardId = handCard.CardID,
                TargetId = req.TargetInstanceID!,
            }.ToDictionary(),
        });

        return new ActionResult { Events = events, StateUpdated = true };
    }

    private static void ValidatePlayPosition(CardDefinition cardDef, Field field, PlayCardRequest req)
    {
        if (req.Index < 0 || req.Index >= BattleConstants.SlotsPerZone)
        {
            throw new GameRuleException($"invalid slot index {req.Index}");
        }

        switch (req.Zone)
        {
            case GameConstants.ZoneFrontend:
                if (!FieldHelpers.IsFrontendEligible(cardDef.CardType))
                {
                    throw new GameRuleException($"{cardDef.CardType} cannot be placed in frontend");
                }
                if (field.Frontend[req.Index] is not null)
                {
                    throw new GameRuleException($"frontend slot {req.Index} is occupied");
                }
                break;

            case GameConstants.ZoneBackend:
                if (!FieldHelpers.IsBackendEligible(cardDef.CardType))
                {
                    throw new GameRuleException($"{cardDef.CardType} cannot be placed in backend");
                }
                if (field.Backend[req.Index] is not null)
                {
                    throw new GameRuleException($"backend slot {req.Index} is occupied");
                }
                break;

            case GameConstants.ZoneSupport:
                if (!FieldHelpers.IsSupportType(cardDef.CardType))
                {
                    throw new GameRuleException($"{cardDef.CardType} cannot be placed in support");
                }
                if (field.Support[req.Index] is not null)
                {
                    throw new GameRuleException($"support slot {req.Index} is occupied");
                }
                break;

            default:
                throw new GameRuleException($"unknown zone: {req.Zone}");
        }
    }

    private static (bool Cancelled, List<GameEvent> Events) FireDeployReactives(
        PlayContext ctx, DeployedResource deployed)
    {
        if (ctx.Effects is null) { return (false, []); }

        var opponentNum = ctx.State.OpponentOf(ctx.PlayerNum);
        var oppField = ctx.State.GetField(opponentNum);
        var allEvents = new List<GameEvent>();

        // リアクティブは1つだけ発動する（セットが最も早いもの）
        var reactive = FieldHelpers.AllSupports(oppField)
            .Where(s => ctx.Effects.Has(s.CardID, TriggerType.OnEnemyDeploy))
            .MinBy(s => s.DeployOrder);

        if (reactive is null) { return (false, allEvents); }

        var handler = ctx.Effects.Get(reactive.CardID, TriggerType.OnEnemyDeploy)!;
        var effectCtx = new EffectContext
        {
            State = ctx.State,
            Game = ctx.Game,
            PlayerNum = opponentNum,
            SupSource = reactive,
            Target = deployed,
            CardCache = ctx.CC,
        };

        var result = handler(effectCtx);
        allEvents.AddRange(result.Events);

        // 発動時に表向きにしてからトラッシュへ送る
        reactive.FaceUp = true;
        FieldHelpers.RemoveSupportFromField(oppField, reactive.InstanceID);
        CardMoveHelpers.AddToTrash(ctx.State, opponentNum, reactive.CardID, reactive.InstanceID, reactive.ArtNo);

        return (result.CancelAction, allEvents);
    }
}
