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
        ICardCache CC, IEffectRegistry Effects);

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
        PlayCardRequest req, ICardCache cc, IEffectRegistry effects)
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

        if (cardDef.CardType == CardTypes.Incident)
        {
            if (TurnManager.IsFirstTurn(state.CurrentTurn))
            {
                throw new GameRuleException("cannot play incident on first turn");
            }
            if (state.GetIncidentPlayedThisTurn(playerNum))
            {
                throw new GameRuleException("incident already played this turn");
            }
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

        return new ActionResult { Events = events };
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

            // リアクティブで本体（Ignition）をキャンセルしうるため、本体より先に解決する。
            var (cancelled, reactiveEvents) = FireOnIncident(ctx, cardDef);
            events.AddRange(reactiveEvents);
            incidentCancelled = cancelled;
        }

        if (!incidentCancelled && ctx.Effects.Has(cardDef.CardId, TriggerType.Ignition))
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
                Trigger = TriggerType.Ignition,
                EffectCardId = cardDef.CardId,
                EffectInstanceId = instanceID,
            };
            var effectResult = handler(effectCtx);
            events.AddRange(effectResult.Events);
            if (effectResult.PendingChoice is { } pendingChoice)
            {
                ctx.State.PendingEffectChoice = pendingChoice;
            }
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

        return new ActionResult { Events = events };
    }

    private static void PlaceSupport(
        PlayContext ctx, Field field,
        CardDefinition cardDef, UndeployedCard handCard, PlayCardRequest req,
        long deployOrder, List<GameEvent> events)
    {
        if (field.Support[req.Index] is not null)
        {
            FieldHelpers.DestroySupport(
                ctx.State, ctx.Game, ctx.PlayerNum, field, field.Support[req.Index]!.InstanceID, ctx.CC, ctx.Effects);
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

        var (onSetEvents, onSetChoice) = FireOnSet(
            ctx, cardDef.CardId, support.InstanceID, source: null, support, req);
        events.AddRange(onSetEvents);

        // カウントダウンなしで稼働した Support 自身の効果を発火（on_deploy 2 段解決）。
        if (support.DeployingTurnsLeft <= 0)
        {
            OnSetFiring.RejectDeferredChoiceBeforeDeployCompletion(onSetChoice, cardDef.CardId);

            events.AddRange(DeployCompletion.CompleteSupport(
                ctx.State, ctx.Game, ctx.PlayerNum, support, ctx.CC, ctx.Effects));
            return;
        }

        PassiveRecalculator.Recalculate(ctx.State, ctx.Game, ctx.CC, ctx.Effects);
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

        var (onSetEvents, onSetChoice) = FireOnSet(
            ctx, cardDef.CardId, resource.InstanceID, resource, supSource: null, req);
        events.AddRange(onSetEvents);

        // 表向きになった時点で on_deploy を発動する仕様のため、デプロイ中はスキップ（実際の発動は DrawPhaseProcessor）。
        if (resource.DeployingTurnsLeft > 0)
        {
            PassiveRecalculator.Recalculate(ctx.State, ctx.Game, ctx.CC, ctx.Effects);
            return false;
        }

        OnSetFiring.RejectDeferredChoiceBeforeDeployCompletion(onSetChoice, cardDef.CardId);

        // 配置時効果が残デプロイターンを 0 まで縮めた場合もその場で稼働にあたるため、表向きにしてから稼働開始処理へ渡す。
        resource.FaceUp = true;

        var (cancelled, deployEvents) = DeployCompletion.CompleteResource(
            ctx.State, ctx.Game, ctx.PlayerNum, resource, ctx.CC, ctx.Effects);
        events.AddRange(deployEvents);

        return cancelled;
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
            FieldHelpers.DestroySupport(
                ctx.State, ctx.Game, ctx.PlayerNum, field, field.Support[req.Index]!.InstanceID, ctx.CC, ctx.Effects);
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

        var (onSetEvents, onSetChoice) = FireOnSet(
            ctx, cardDef.CardId, attachInstanceID, target, attachment, req);
        events.AddRange(onSetEvents);

        // アタッチメントは装備の時点で必ず稼働するため、配置時効果が選択待ちのまま先へ進めない。
        OnSetFiring.RejectDeferredChoiceBeforeDeployCompletion(onSetChoice, cardDef.CardId);

        // アタッチメントは装備された時点が自身のデプロイにあたるため、ここで自身の on_deploy 効果を発火する。
        if (ctx.Effects.Has(cardDef.CardId, TriggerType.OnDeploy))
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
                Trigger = TriggerType.OnDeploy,
                EffectCardId = cardDef.CardId,
                EffectInstanceId = attachInstanceID,
            };
            var effectResult = handler(effectCtx);
            events.AddRange(effectResult.Events);
        }

        PassiveRecalculator.Recalculate(ctx.State, ctx.Game, ctx.CC, ctx.Effects);

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

        return new ActionResult { Events = events };
    }

    /// <summary>
    /// 場に置かれたカード自身の配置時効果を発火し、選択待ちになったらゲーム状態に載せます。
    /// </summary>
    /// <param name="ctx">カードプレイ処理コンテキスト。</param>
    /// <param name="cardId">置いたカードのカード ID。</param>
    /// <param name="instanceId">置いたカードのインスタンス ID。</param>
    /// <param name="source">効果の発火元リソース。アタッチメントでは装備先のリソース、サポートカードでは null。</param>
    /// <param name="supSource">効果の発火元サポートカード。リソースでは null。</param>
    /// <param name="req">プレイヤーの選択値を含むカードプレイリクエスト。</param>
    /// <returns>発火したイベントと、効果が選択を要求して中断した場合の選択待ち。</returns>
    private static (List<GameEvent> Events, PendingEffectChoice? PendingChoice) FireOnSet(
        PlayContext ctx, string cardId, string instanceId,
        DeployedResource? source, DeployedSupport? supSource, PlayCardRequest req)
    {
        var (events, pendingChoice) = OnSetFiring.Fire(
            ctx.State, ctx.Game, ctx.PlayerNum, cardId, instanceId,
            source, supSource, req.ChoiceData, ctx.CC, ctx.Effects);

        if (pendingChoice is not null)
        {
            ctx.State.PendingEffectChoice = pendingChoice;
        }

        return (events, pendingChoice);
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
    /// 両プレイヤーのサポートゾーンとフィールドリソースの on_incident を発火します
    /// </summary>
    /// <param name="ctx">カードプレイ処理コンテキスト。</param>
    /// <param name="incidentCard">使用されたインシデントカードの定義。</param>
    /// <returns>アクションがキャンセルされたかと、発火したイベント。</returns>
    private static (bool Cancelled, List<GameEvent> Events) FireOnIncident(
        PlayContext ctx, CardDefinition incidentCard)
    {

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
                ctx.State, ctx.Game, ctx.Effects, ctx.CC, TriggerType.OnIncident, candidates,
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
                    Trigger = TriggerType.OnIncident,
                    EffectCardId = candidate.CardId,
                });

            allEvents.AddRange(events);
            cancelled |= zoneCancelled;
        }

        return (cancelled, allEvents);
    }
}
