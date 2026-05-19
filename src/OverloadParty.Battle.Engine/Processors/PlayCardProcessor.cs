using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// PlayCardProcessor はリソース・サポート・アタッチメントをフィールドに配置するカードプレイアクションを処理します
/// </summary>
public static class PlayCardProcessor
{
    /// <summary>カードプレイ処理で各メソッドへ引き回す処理コンテキスト。</summary>
    private record PlayContext(
        BattleGameState State, Game Game, long PlayerNum,
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
        BattleGameState state, Game game, long playerNum,
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

        events.Insert(0, new GameEvent
        {
            GameID = game.GameID,
            EventType = ActionTypes.PlayCard,
            PlayerNum = playerNum,
            EventData = new PlayCardEventData
            {
                CardId = handCard.CardID,
                Zone = req.Zone,
                Index = req.Index,
                Cancelled = cancelled ? true : null,
            },
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

        var events = new List<GameEvent>();
        bool incidentCancelled = false;

        if (cardDef.CardType == CardTypes.Incident)
        {
            ctx.State.SetIncidentPlayedThisTurn(ctx.PlayerNum, true);

            // インシデント使用に反応する on_incident を発火（本体 ops より前）。
            var (cancelled, reactiveEvents) = FireOnIncident(ctx, cardDef);
            events.AddRange(reactiveEvents);
            incidentCancelled = cancelled;
        }

        if (!incidentCancelled && ctx.Effects?.Has(cardDef.CardId, TriggerType.Ignition) == true)
        {
            var handler = ctx.Effects.Get(cardDef.CardId, TriggerType.Ignition)!;
            var effectCtx = new EffectContext
            {
                State = ctx.State,
                Game = ctx.Game,
                PlayerNum = ctx.PlayerNum,
                CardCache = ctx.CC,
                ChoiceData = req.ChoiceData,
                Effects = ctx.Effects,
            };
            var effectResult = handler(effectCtx);
            events.AddRange(effectResult.Events);
        }

        CardMoveHelpers.AddToTrash(ctx.State, ctx.PlayerNum, cardDef.CardId, instanceID, handCard.ArtNo);

        events.Insert(0, new GameEvent
        {
            GameID = ctx.Game.GameID,
            EventType = ActionTypes.PlayCard,
            PlayerNum = ctx.PlayerNum,
            EventData = new PlayCardEventData
            {
                CardId = handCard.CardID,
                Zone = "",
                Index = -1,
                Cancelled = incidentCancelled ? true : null,
            },
        });

        return new ActionResult { Events = events, StateUpdated = true };
    }

    private static void PlaceSupport(
        PlayContext ctx, Field field,
        CardDefinition cardDef, UndeployedCard handCard, PlayCardRequest req,
        long deployOrder, List<GameEvent> events)
    {
        if (field.Support[req.Index] is not null)
        {
            FieldHelpers.DestroySupport(ctx.State, ctx.PlayerNum, field, field.Support[req.Index]!.InstanceID);
        }

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

        // カウントダウンなしで稼働した Support 自身の効果を発火（on_deploy 2 段解決）。
        if (support.DeployingTurnsLeft <= 0)
        {
            events.AddRange(FireOnDeployForSupport(ctx, support));
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

        if (req.Zone == Zones.Frontend)
        {
            field.Frontend[req.Index] = resource;
        }
        else
        {
            field.Backend[req.Index] = resource;
        }

        // deploy_turns > 0 のリソースは裏向きセット段階であり on_deploy event ではない。
        // カウントダウン完了時に DrawPhaseProcessor が発火する。
        if (resource.DeployingTurnsLeft > 0)
        {
            FieldChangeTrigger.Fire(ctx.State, ctx.Game, ctx.CC, ctx.Effects);
            return false;
        }

        ctx.State.SetHasHadActiveResource(ctx.PlayerNum, true);

        // 相手の on_deploy 誘発はデプロイをキャンセルしうるため先に解決し、
        // キャンセルされなかった場合のみデプロイされたカード自身の効果を走らせる（2 段解決）。
        var (cancelled, deployEvents) = FireOnDeployForResource(ctx, resource);
        events.AddRange(deployEvents);

        if (cancelled)
        {
            FieldHelpers.RemoveResourceFromField(field, resource.InstanceID);
            CardMoveHelpers.AddToTrash(ctx.State, ctx.PlayerNum, cardDef.CardId, resource.InstanceID, resource.ArtNo);
            return true;
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
        if (req.Zone != Zones.Support)
        {
            throw new GameRuleException("attachment must be placed in support zone");
        }
        if (!ZoneValidator.IsValidSlotIndex(field, Zones.Support, req.Index))
        {
            throw new GameRuleException($"invalid support slot index {req.Index}");
        }
        if (field.Support[req.Index] is not null)
        {
            FieldHelpers.DestroySupport(ctx.State, ctx.PlayerNum, field, field.Support[req.Index]!.InstanceID);
        }

        var attachInstanceID = ctx.State.NextInstanceID();
        var attachment = new DeployedSupport
        {
            InstanceID = attachInstanceID,
            CardID = cardDef.CardId,
            ArtNo = handCard.ArtNo,
            TargetInstanceID = target.InstanceID,
            DeployOrder = ctx.State.NextDeployOrder(),
            FaceUp = true,
        };
        field.Support[req.Index] = attachment;

        hand.RemoveAt(handIdx);

        var events = new List<GameEvent>();

        // アタッチメントは装備された時点が自身のデプロイにあたるため、ここで自身の on_deploy 効果を発火する。
        if (ctx.Effects?.Has(cardDef.CardId, TriggerType.OnDeploy) == true)
        {
            var handler = ctx.Effects.Get(cardDef.CardId, TriggerType.OnDeploy)!;
            var effectCtx = new EffectContext
            {
                State = ctx.State,
                Game = ctx.Game,
                PlayerNum = ctx.PlayerNum,
                Source = target,
                SupSource = attachment,
                Target = target,
                EventOwnerNum = ctx.PlayerNum,
                CardCache = ctx.CC,
                ChoiceData = req.ChoiceData,
                Effects = ctx.Effects,
            };
            var effectResult = handler(effectCtx);
            events.AddRange(effectResult.Events);
        }

        FieldChangeTrigger.Fire(ctx.State, ctx.Game, ctx.CC, ctx.Effects);

        events.Insert(0, new GameEvent
        {
            GameID = ctx.Game.GameID,
            EventType = EventTypes.AttachCard,
            PlayerNum = ctx.PlayerNum,
            EventData = new AttachCardEventData
            {
                CardId = handCard.CardID,
                TargetId = req.TargetInstanceID!,
            },
        });

        return new ActionResult { Events = events, StateUpdated = true };
    }

    private static void ValidatePlayPosition(CardDefinition cardDef, Field field, PlayCardRequest req)
    {
        if (req.Zone is not (Zones.Frontend or Zones.Backend or Zones.Support))
        {
            throw new GameRuleException($"unknown zone: {req.Zone}");
        }
        if (!ZoneValidator.IsValidSlotIndex(field, req.Zone, req.Index))
        {
            throw new GameRuleException($"invalid slot index {req.Index}");
        }
        if (!ZoneValidator.IsZoneEligible(cardDef, req.Zone))
        {
            throw new GameRuleException($"{cardDef.CardType} cannot be placed in {req.Zone}");
        }
        if (!ZoneValidator.IsSlotEmpty(field, req.Zone, req.Index))
        {
            throw new GameRuleException($"{req.Zone} slot {req.Index} is occupied");
        }
    }

    /// <summary>
    /// デプロイされたリソースの on_deploy を発火します。
    /// </summary>
    /// <param name="ctx">カードプレイ処理コンテキスト。</param>
    /// <param name="deployed">デプロイされたリソース。</param>
    /// <returns>デプロイがキャンセルされたかと、発火したイベント。</returns>
    private static (bool Cancelled, List<GameEvent> Events) FireOnDeployForResource(
        PlayContext ctx, DeployedResource deployed)
    {
        if (ctx.Effects is null) { return (false, []); }

        var events = new List<GameEvent>();

        var (cancelled, triggerEvents) = FireOnDeployTriggers(ctx, deployed, supSource: null);
        events.AddRange(triggerEvents);

        if (cancelled) { return (true, events); }

        // Stage 2: デプロイされたカード自身の効果。
        if (ctx.Effects.Has(deployed.CardID, TriggerType.OnDeploy))
        {
            var handler = ctx.Effects.Get(deployed.CardID, TriggerType.OnDeploy)!;
            var result = handler(new EffectContext
            {
                State = ctx.State,
                Game = ctx.Game,
                PlayerNum = ctx.PlayerNum,
                Source = deployed,
                Target = deployed,
                EventOwnerNum = ctx.PlayerNum,
                CardCache = ctx.CC,
                Effects = ctx.Effects,
            });
            events.AddRange(result.Events);
        }

        return (false, events);
    }

    /// <summary>
    /// デプロイされたサポートカードの on_deploy を発火します。
    /// </summary>
    /// <param name="ctx">カードプレイ処理コンテキスト。</param>
    /// <param name="deployed">デプロイされたサポートカード。</param>
    /// <returns>発火したイベント。</returns>
    private static List<GameEvent> FireOnDeployForSupport(PlayContext ctx, DeployedSupport deployed)
    {
        if (ctx.Effects is null) { return []; }

        var events = new List<GameEvent>();

        var (cancelled, triggerEvents) = FireOnDeployTriggers(ctx, deployedResource: null, deployed);
        events.AddRange(triggerEvents);

        if (cancelled) { return events; }

        if (ctx.Effects.Has(deployed.CardID, TriggerType.OnDeploy))
        {
            var handler = ctx.Effects.Get(deployed.CardID, TriggerType.OnDeploy)!;
            var result = handler(new EffectContext
            {
                State = ctx.State,
                Game = ctx.Game,
                PlayerNum = ctx.PlayerNum,
                SupSource = deployed,
                EventOwnerNum = ctx.PlayerNum,
                CardCache = ctx.CC,
                Effects = ctx.Effects,
            });
            events.AddRange(result.Events);
        }

        return events;
    }

    /// <summary>
    /// on_deploy の Stage 1 として相手サポートゾーンの誘発を発火します
    /// </summary>
    /// <param name="ctx">カードプレイ処理コンテキスト。</param>
    /// <param name="deployedResource">デプロイされたリソース（サポートカードのデプロイ時は null）。</param>
    /// <param name="supSource">デプロイされたサポートカード（リソースのデプロイ時は null）。</param>
    /// <returns>デプロイがキャンセルされたかと、発火したイベント。</returns>
    private static (bool Cancelled, List<GameEvent> Events) FireOnDeployTriggers(
        PlayContext ctx, DeployedResource? deployedResource, DeployedSupport? supSource)
    {
        if (ctx.Effects is null) { return (false, []); }

        var opponentNum = ctx.State.OpponentOf(ctx.PlayerNum);
        var opponentField = ctx.State.GetField(opponentNum);

        var candidates = FieldHelpers.AllSupports(opponentField)
            .Select(s => EventTriggerCandidate.ForSupport(s, opponentNum))
            .ToList();

        return EventTriggerFiring.Fire(
            ctx.State, ctx.Effects, ctx.CC, TriggerType.OnDeploy, candidates,
            candidate => new EffectContext
            {
                State = ctx.State,
                Game = ctx.Game,
                PlayerNum = opponentNum,
                SupSource = candidate.Support,
                Source = deployedResource,
                Target = deployedResource,
                EventOwnerNum = ctx.PlayerNum,
                CardCache = ctx.CC,
                Effects = ctx.Effects,
            });
    }

    /// <summary>
    /// 両プレイヤーのサポートゾーンとフィールドリソースの on_incident を発火します
    /// </summary>
    /// <param name="ctx">カードプレイ処理コンテキスト。</param>
    /// <param name="incidentCard">使用されたインシデントカードの定義。</param>
    /// <returns>アクションがキャンセルされたかと、発火したイベント。</returns>
    private static (bool Cancelled, List<GameEvent> Events) FireOnIncident(
        PlayContext ctx, CardDefinition incidentCard)
    {
        if (ctx.Effects is null) { return (false, []); }

        var allEvents = new List<GameEvent>();
        bool cancelled = false;

        foreach (long ownerNum in new[] { ctx.PlayerNum, ctx.State.OpponentOf(ctx.PlayerNum) })
        {
            var field = ctx.State.GetField(ownerNum);
            var candidates = new List<EventTriggerCandidate>();

            foreach (var sup in FieldHelpers.AllSupports(field))
            {
                candidates.Add(EventTriggerCandidate.ForSupport(sup, ownerNum));
            }
            foreach (var res in FieldHelpers.AllFaceUpResources(field))
            {
                candidates.Add(EventTriggerCandidate.ForResource(res, ownerNum));
            }

            var (zoneCancelled, events) = EventTriggerFiring.Fire(
                ctx.State, ctx.Effects, ctx.CC, TriggerType.OnIncident, candidates,
                candidate => new EffectContext
                {
                    State = ctx.State,
                    Game = ctx.Game,
                    PlayerNum = ownerNum,
                    Source = candidate.Resource,
                    SupSource = candidate.Support,
                    Target = candidate.Resource,
                    EventOwnerNum = ctx.PlayerNum,
                    IncidentCard = incidentCard,
                    CardCache = ctx.CC,
                    Effects = ctx.Effects,
                });

            allEvents.AddRange(events);
            cancelled |= zoneCancelled;
        }

        return (cancelled, allEvents);
    }
}
