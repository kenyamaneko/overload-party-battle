using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// YAML-config-driven NPC strategy.
/// All decision parameters are read from AiConfig.
/// </summary>
public class NpcAi : INpcStrategy
{
    private readonly AiConfig _config;
    private readonly ICardCache _cc;
    private readonly IEffectRegistry _effects;

    public NpcAi(AiConfig config, ICardCache cc, IEffectRegistry effects)
    {
        _config = config;
        _cc = cc;
        _effects = effects;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Main Phase
    // ═══════════════════════════════════════════════════════════════

    public List<NpcAction> DecideMainPhaseActions(
        BattleGameState state, Game game, long npcPlayerNum, List<AvailableAction> available)
    {
        var field = state.GetField(npcPlayerNum);
        var oppField = state.GetField(state.OpponentOf(npcPlayerNum));
        var hand = state.GetHand(npcPlayerNum);
        var budget = state.GetBudget(npcPlayerNum);

        var ctx = new DecisionContext(field, oppField, hand, budget, _cc)
        {
            CurrentTurn = state.CurrentTurn,
        };

        var activeConfig = ResolveActiveConfig(ctx);
        var usedZones = new HashSet<string>();
        var actions = new List<NpcAction>();

        actions.AddRange(DoImmediateActions(ctx, available, usedZones, activeConfig));
        actions.AddRange(DoDeployActions(ctx, available, usedZones));
        actions.AddRange(DoActivateEffects(ctx, available, activeConfig));
        actions.AddRange(DoScaleUpActions(ctx, available));
        var insightPool = state.GetInsightPool(npcPlayerNum);
        if (insightPool > 0)
        {
            actions.AddRange(DoMonetizeActions(ctx, available, insightPool));
        }

        actions.Add(MakeEndPhaseAction());
        return actions;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Battle Phase
    // ═══════════════════════════════════════════════════════════════

    public List<NpcAction> DecideBattlePhaseActions(
        BattleGameState state, Game game, long npcPlayerNum, List<AvailableAction> available)
    {
        var selfField = state.GetField(npcPlayerNum);
        var oppField = state.GetField(state.OpponentOf(npcPlayerNum));
        var attackActions = ActionFilter.FilterByType(available, ActionTypes.Attack);
        var actions = new List<NpcAction>();

        foreach (var a in attackActions)
        {
            var target = ResolveAttackTarget(a.ValidTargets, selfField, oppField);
            actions.Add(new NpcAction
            {
                ActionType = ActionTypes.Attack,
                Data = new AttackRequest
                {
                    AttackerInstanceID = a.SourceInstanceID!,
                    TargetInstanceID = target,
                },
            });
        }

        actions.Add(MakeEndPhaseAction());
        return actions;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Discard
    // ═══════════════════════════════════════════════════════════════

    public List<string> DecideDiscard(BattleGameState state, long npcPlayerNum, int discardCount)
    {
        var hand = state.GetHand(npcPlayerNum);
        var field = state.GetField(npcPlayerNum);
        var oppField = state.GetField(state.OpponentOf(npcPlayerNum));
        var budget = state.GetBudget(npcPlayerNum);
        var ctx = new DecisionContext(field, oppField, hand, budget, _cc)
        {
            CurrentTurn = state.CurrentTurn,
        };

        return hand
            .OrderBy(h => EvaluateCardKeepPriority(h, ctx))
            .Take(discardCount)
            .Select(h => h.InstanceID)
            .ToList();
    }

    /// <summary>
    /// 手札のカードを「残したい度」で評価。(TypeRank, Priority) のタプルが低いほど先に捨てる。
    ///
    /// 捨てる順:
    ///   アタッチメント → プラットフォーム → リアクティブ → リソース → インシデント → ストラテジー
    /// 同タイプ内は config の優先度が低いものから捨てる。
    ///
    /// アタッチメント/プラットフォーム/リアクティブが手札に残っている
    /// = フィールドが埋まっていてすぐに出せない可能性が高いので先に捨てる。
    /// インシデント/ストラテジーはいつでも使えるカードなので、
    /// 手札に残しているのはタイミングを狙っている可能性が高く、最後まで残す。
    /// </summary>
    private (int TypeRank, int Priority) EvaluateCardKeepPriority(UndeployedCard handCard, DecisionContext ctx)
    {
        var card = ResolveCard(handCard.CardID);

        return card.CardType switch
        {
            CardTypes.Attachment => (0, GetAttachmentPriority(card)),
            CardTypes.Platform => (1, ResolveDeployPriority(card, ctx)),
            CardTypes.Reactive => (2, GetReactivePriority(card)),
            CardTypes.Incident => (4, 0),
            CardTypes.Strategy => (5, 0),
            // リソース（Compute, Database, ObjectStorage 等）
            _ when !FieldHelpers.IsImmediateType(card.CardType) => (3, ResolveDeployPriority(card, ctx)),
            _ => throw new InvalidOperationException(
                $"Unexpected card type '{card.CardType}' for card '{card.CardId}' in discard evaluation"),
        };
    }

    private int GetAttachmentPriority(CardDefinition card)
    {
        if (_config.Attachments is null || !_config.Attachments.TryGetValue(card.CardId, out var entry))
        {
            throw new InvalidOperationException(
                $"No attachment config for card '{card.CardId}' in model '{_config.Model}'");
        }
        return entry.Priority;
    }

    private int GetReactivePriority(CardDefinition card)
    {
        if (_config.Reactive is null || !_config.Reactive.Priorities.TryGetValue(card.CardId, out var pri))
        {
            throw new InvalidOperationException(
                $"No reactive config for card '{card.CardId}' in model '{_config.Model}'");
        }
        return pri;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Slot Select
    // ═══════════════════════════════════════════════════════════════

    public NpcAction? DecideSlotSelect(BattleGameState state, long npcPlayerNum)
    {
        var pending = state.PendingSlotSelects.FirstOrDefault();
        if (pending is null || pending.PlayerNum != npcPlayerNum || pending.ValidZones.Count == 0)
        {
            return null;
        }

        var zone = ActionFilter.ParseZoneStr(pending.ValidZones[0])
            ?? throw new InvalidOperationException(
                $"Failed to parse zone '{pending.ValidZones[0]}' for slot select");

        return new NpcAction
        {
            ActionType = ActionTypes.SelectSlot,
            Data = new SelectSlotRequest
            {
                Zone = zone.Zone,
                Index = zone.Index,
            },
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  Immediate cards (Strategy/Incident)
    // ═══════════════════════════════════════════════════════════════

    private List<NpcAction> DoImmediateActions(
        DecisionContext ctx, List<AvailableAction> available,
        HashSet<string> usedZones, AiConfig activeConfig)
    {
        var playActions = ActionFilter.FilterByType(available, ActionTypes.PlayCard);

        var candidates = new List<(AvailableAction Action, int Priority, Dictionary<string, object>? Choice)>();
        foreach (var a in playActions)
        {
            var card = ResolveCard(a.CardID);
            if (!FieldHelpers.IsImmediateType(card.CardType))
            {
                continue;
            }

            // hold_until: 条件が満たされるまで特定カードタイプを使わない
            if (ShouldHoldImmediate(card, ctx, activeConfig))
            {
                continue;
            }

            // use_conditions: カテゴリ固有の条件をチェック
            if (!CheckImmediateUseConditions(card, ctx, activeConfig))
            {
                continue;
            }

            var (pri, use, choice) = PriorityResolver.Evaluate(
                card.CardId, TriggerType.Activate, ctx, activeConfig, _effects, _cc);
            if (!use)
            {
                continue;
            }

            if (a.EffectTargetType == "Choice" && choice is null)
            {
                if (!(a.ValidTargets?.Count > 0)) { continue; }
                choice = new Dictionary<string, object> { ["instanceId"] = a.ValidTargets[0] };
            }

            candidates.Add((a, pri, choice));
        }

        candidates.Sort((a, b) => b.Priority.CompareTo(a.Priority));

        var actions = new List<NpcAction>();
        foreach (var c in candidates)
        {
            var zone = ActionFilter.PickSupportZone(c.Action.ValidZones, usedZones);
            if (zone is null)
            {
                continue;
            }

            var pos = ActionFilter.ParseZoneStr(zone)!;
            actions.Add(new NpcAction
            {
                ActionType = ActionTypes.PlayCard,
                Data = new PlayCardRequest
                {
                    CardInstanceID = c.Action.HandInstanceID!,
                    Zone = pos.Zone,
                    Index = pos.Index,
                    ChoiceData = c.Choice,
                },
            });
            usedZones.Add(zone);
        }
        return actions;
    }

    private bool ShouldHoldImmediate(CardDefinition card, DecisionContext ctx, AiConfig config)
    {
        if (config.ImmediateCards.HoldUntil is null)
        {
            return false;
        }

        foreach (var hold in config.ImmediateCards.HoldUntil)
        {
            if (hold.CardType == card.CardType?.ToLowerInvariant() ||
                hold.CardType == card.CardType)
            {
                if (!GuardChecker.Check(hold.Condition, ctx, _cc))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool CheckImmediateUseConditions(CardDefinition card, DecisionContext ctx, AiConfig config)
    {
        if (config.ImmediateCards.UseConditions is null)
        {
            return true;
        }

        var info = _effects.GetEffectInfo(card.CardId, TriggerType.Activate);
        if (info is null)
        {
            return true;
        }

        foreach (var cat in info.Categories)
        {
            var catKey = PriorityResolver.CategoryToKey(cat);
            if (catKey is not null &&
                config.ImmediateCards.UseConditions.TryGetValue(catKey, out var conditions))
            {
                if (!GuardChecker.CheckAll(conditions, ctx, _cc))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Deploy
    // ═══════════════════════════════════════════════════════════════

    private List<NpcAction> DoDeployActions(
        DecisionContext ctx, List<AvailableAction> available, HashSet<string> usedZones)
    {
        var playActions = ActionFilter.FilterByType(available, ActionTypes.PlayCard);

        var candidates = new List<(AvailableAction Action, CardDefinition Card, int Priority)>();
        foreach (var a in playActions)
        {
            var card = ResolveCard(a.CardID);
            if (FieldHelpers.IsImmediateType(card.CardType))
            {
                continue;
            }
            if (card.CardType == CardTypes.Attachment)
            {
                continue;
            }
            // リアクティブ cards handled separately
            if (card.CardType == CardTypes.Reactive)
            {
                continue;
            }

            int pri = ResolveDeployPriority(card, ctx);
            candidates.Add((a, card, pri));
        }

        candidates.Sort((a, b) => b.Priority.CompareTo(a.Priority));

        var actions = new List<NpcAction>();
        var deployed = new HashSet<string>();
        var addedMaintenanceCost = 0L;

        foreach (var c in candidates)
        {
            if (deployed.Contains(c.Action.HandInstanceID!))
            {
                continue;
            }

            // 維持費上限チェック
            if (c.Card.MaintenanceCost > 0 &&
                WouldExceedMaintenanceLimit(ctx, addedMaintenanceCost + c.Card.MaintenanceCost))
            {
                continue;
            }

            var zone = PickDeployZone(c.Action.ValidZones, c.Card, usedZones);
            if (zone is null)
            {
                continue;
            }

            var pos = ActionFilter.ParseZoneStr(zone)!;
            var req = new PlayCardRequest
            {
                CardInstanceID = c.Action.HandInstanceID!,
                Zone = pos.Zone,
                Index = pos.Index,
            };
            if (c.Action.ChoiceOptions?.Count > 0)
            {
                var choice = ResolveDeployChoice(c.Card.CardId);
                req.ChoiceData = new Dictionary<string, object> { ["option"] = choice };
            }

            actions.Add(new NpcAction { ActionType = ActionTypes.PlayCard, Data = req });
            deployed.Add(c.Action.HandInstanceID!);
            usedZones.Add(zone);
            addedMaintenanceCost += c.Card.MaintenanceCost;
        }

        // リソースの後にアタッチメントをデプロイ
        if (_config.Attachments is not null)
        {
            actions.AddRange(DoAttachmentDeploy(ctx, playActions, usedZones));
        }

        // リアクティブカードをデプロイ
        if (_config.Reactive is not null)
        {
            actions.AddRange(DoReactiveDeploy(playActions, usedZones));
        }

        return actions;
    }

    private int ResolveDeployPriority(CardDefinition card, DecisionContext ctx)
    {
        if (_config.Deploy.ConditionalPriorities is not null)
        {
            foreach (var cp in _config.Deploy.ConditionalPriorities)
            {
                if (cp.CardId != card.CardId)
                {
                    continue;
                }

                return GuardChecker.Check(cp.Condition, ctx, _cc)
                    ? cp.Priority
                    : cp.FallbackPriority;
            }
        }

        foreach (var entry in _config.Deploy.Priorities)
        {
            if (entry.CardId is not null && entry.CardId == card.CardId)
            {
                return entry.Priority;
            }
        }

        foreach (var entry in _config.Deploy.Priorities)
        {
            if (entry.CardType is not null && MatchesCardType(card, entry.CardType))
            {
                return entry.Priority;
            }
        }

        return 0;
    }

    private string? PickDeployZone(List<string>? validZones, CardDefinition card, HashSet<string> usedZones)
    {
        if (validZones is null)
        {
            return null;
        }

        // Use zone_preferences from config if available
        if (_config.Deploy.ZonePreferences is not null)
        {
            var typeKey = card.CardType?.ToLowerInvariant() ?? "";
            if (card.CardType == CardTypes.ObjectStorage)
            {
                typeKey = "object_storage";
            }
            else if (card.IsComputeType)
            {
                typeKey = "compute";
            }
            else if (card.IsDataType)
            {
                typeKey = "data";
            }
            else if (card.IsSupportType)
            {
                typeKey = "platform";
            }

            if (_config.Deploy.ZonePreferences.TryGetValue(typeKey, out var prefs))
            {
                foreach (var pref in prefs)
                {
                    var match = validZones.FirstOrDefault(z =>
                        z.StartsWith(pref + "_") && !usedZones.Contains(z));
                    if (match is not null)
                    {
                        return match;
                    }
                }
            }
        }

        // Fallback to existing ActionFilter logic
        return ActionFilter.PickBestZone(validZones, card, usedZones);
    }

    private string ResolveDeployChoice(string cardId)
    {
        if (_config.Deploy.Choices is not null &&
            _config.Deploy.Choices.TryGetValue(cardId, out var choice))
        {
            return choice;
        }

        throw new InvalidOperationException(
            $"No deploy choice configured for card '{cardId}' in model '{_config.Model}'");
    }

    // ─── Attachments ────────────────────────────────────────────

    private List<NpcAction> DoAttachmentDeploy(
        DecisionContext ctx, List<AvailableAction> playActions, HashSet<string> usedZones)
    {
        var actions = new List<NpcAction>();
        var attachCandidates = new List<(AvailableAction Action, CardDefinition Card, int Priority)>();

        foreach (var a in playActions)
        {
            var card = ResolveCard(a.CardID);
            if (card.CardType != CardTypes.Attachment)
            {
                continue;
            }

            int pri = 0;
            if (_config.Attachments!.TryGetValue(card.CardId, out var entry))
            {
                pri = entry.Priority;
            }

            attachCandidates.Add((a, card, pri));
        }

        attachCandidates.Sort((a, b) => b.Priority.CompareTo(a.Priority));

        foreach (var c in attachCandidates)
        {
            if (!(c.Action.ValidTargets?.Count > 0))
            {
                continue;
            }

            // config のターゲット指定でリソースを選ぶ。該当なしなら見送り
            string? targetId = null;
            if (_config.Attachments!.TryGetValue(c.Card.CardId, out var cfg) && cfg.Target is not null)
            {
                targetId = TargetSelector.ResolveFromValid(
                    cfg.Target, c.Action.ValidTargets, ctx.Field, ctx.OppField, _cc);
            }
            else
            {
                targetId = c.Action.ValidTargets.FirstOrDefault();
            }

            if (targetId is null)
            {
                continue;
            }

            var zone = ActionFilter.PickSupportZone(c.Action.ValidZones, usedZones);
            if (zone is null)
            {
                continue;
            }

            var pos = ActionFilter.ParseZoneStr(zone)!;
            actions.Add(new NpcAction
            {
                ActionType = ActionTypes.PlayCard,
                Data = new PlayCardRequest
                {
                    CardInstanceID = c.Action.HandInstanceID!,
                    Zone = pos.Zone,
                    Index = pos.Index,
                    TargetInstanceID = targetId,
                },
            });
            usedZones.Add(zone);
        }

        return actions;
    }

    // ─── Reactive cards ───────────────────────────────────────────

    private List<NpcAction> DoReactiveDeploy(
        List<AvailableAction> playActions, HashSet<string> usedZones)
    {
        var reactive = _config.Reactive!;
        var actions = new List<NpcAction>();

        // サポートゾーンの既存リアクティブカード数を計算
        var usedReactiveSlots = usedZones.Count(z => z.StartsWith("support_"));

        var candidates = new List<(AvailableAction Action, int Priority)>();
        foreach (var a in playActions)
        {
            var card = ResolveCard(a.CardID);
            if (card.CardType != CardTypes.Reactive)
            {
                continue;
            }

            var pri = reactive.Priorities.GetValueOrDefault(card.CardId, 0);
            if (pri <= 0)
            {
                continue;
            }

            candidates.Add((a, pri));
        }

        candidates.Sort((a, b) => b.Priority.CompareTo(a.Priority));

        foreach (var c in candidates)
        {
            if (usedReactiveSlots >= reactive.MaxSlots)
            {
                break;
            }

            var zone = ActionFilter.PickSupportZone(c.Action.ValidZones, usedZones);
            if (zone is null)
            {
                continue;
            }

            var pos = ActionFilter.ParseZoneStr(zone)!;
            actions.Add(new NpcAction
            {
                ActionType = ActionTypes.PlayCard,
                Data = new PlayCardRequest
                {
                    CardInstanceID = c.Action.HandInstanceID!,
                    Zone = pos.Zone,
                    Index = pos.Index,
                },
            });
            usedZones.Add(zone);
            usedReactiveSlots++;
        }

        return actions;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Activate effects
    // ═══════════════════════════════════════════════════════════════

    private List<NpcAction> DoActivateEffects(
        DecisionContext ctx, List<AvailableAction> available, AiConfig activeConfig)
    {
        var activateActions = ActionFilter.FilterByType(available, ActionTypes.UseEffect);

        var candidates = new List<(AvailableAction Action, int Priority, string? TargetId)>();
        foreach (var a in activateActions)
        {
            var cardId = ActionFilter.ResolveCardIdForInstance(a.SourceInstanceID!, ctx.Field);

            var (pri, use, choiceData) = PriorityResolver.Evaluate(
                cardId, TriggerType.Activate, ctx, activeConfig, _effects, _cc);
            if (!use)
            {
                continue;
            }

            string? targetId = choiceData is not null && choiceData.TryGetValue("instanceId", out var id)
                ? id.ToString()
                : null;

            if (a.EffectTargetType == "Choice" && targetId is null)
            {
                if (!(a.ValidTargets?.Count > 0))
                {
                    continue;
                }
                targetId = SelectTargetFromValid(cardId, a.ValidTargets, ctx, activeConfig);
                if (targetId is null)
                {
                    continue;
                }
            }

            candidates.Add((a, pri, targetId));
        }
        candidates.Sort((a, b) => b.Priority.CompareTo(a.Priority));

        var actions = new List<NpcAction>();
        foreach (var c in candidates)
        {
            actions.Add(new NpcAction
            {
                ActionType = ActionTypes.UseEffect,
                Data = new UseEffectRequest
                {
                    InstanceID = c.Action.SourceInstanceID!,
                    TargetInstanceID = c.TargetId,
                },
            });
        }
        return actions;
    }

    private string? SelectTargetFromValid(
        string cardId, List<string> validTargets, DecisionContext ctx, AiConfig activeConfig)
    {
        var validSet = new HashSet<string>(validTargets);
        var info = _effects.GetEffectInfo(cardId, TriggerType.Activate);
        if (info is not null)
        {
            var target = PriorityResolver.SelectTarget(info, ctx, activeConfig.TargetSelection, _cc);
            if (target is not null && validSet.Contains(target))
            {
                return target;
            }
        }

        return validTargets.FirstOrDefault();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scale up
    // ═══════════════════════════════════════════════════════════════

    private List<NpcAction> DoScaleUpActions(DecisionContext ctx, List<AvailableAction> available)
    {
        var scaleActions = ActionFilter.FilterByType(available, ActionTypes.ScaleUp);
        var family = ResolveInstanceFamily(ctx);

        var sorted = TargetSelector.OrderActions(scaleActions, _config.ScaleUp.OrderBy, ctx.Field, _cc);

        var actions = new List<NpcAction>();
        var addedMaintenanceCost = 0L;

        foreach (var a in sorted)
        {
            var cardId = ActionFilter.ResolveCardIdForInstance(a.SourceInstanceID!, ctx.Field);
            var estimatedCostIncrease = ResolveCard(cardId).MaintenanceCost;
            if (estimatedCostIncrease > 0 &&
                WouldExceedMaintenanceLimit(ctx, addedMaintenanceCost + estimatedCostIncrease))
            {
                continue;
            }

            actions.Add(new NpcAction
            {
                ActionType = ActionTypes.ScaleUp,
                Data = new ScaleUpRequest
                {
                    InstanceID = a.SourceInstanceID!,
                    TargetRank = a.TargetRank!,
                    InstanceFamily = a.NeedsFamily ? family : null,
                },
            });
            addedMaintenanceCost += estimatedCostIncrease;
        }
        return actions;
    }

    private string ResolveInstanceFamily(DecisionContext ctx)
    {
        if (_config.ScaleUp.ConditionalFamily is not null)
        {
            foreach (var cf in _config.ScaleUp.ConditionalFamily)
            {
                if (GuardChecker.Check(cf.Condition, ctx, _cc))
                {
                    return cf.Family;
                }
            }
        }
        return _config.ScaleUp.InstanceFamily;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Monetize
    // ═══════════════════════════════════════════════════════════════

    private List<NpcAction> DoMonetizeActions(
        DecisionContext ctx, List<AvailableAction> available, long insightPool)
    {
        var yieldActions = ActionFilter.FilterByType(available, ActionTypes.Monetize);
        if (!yieldActions.Any())
        {
            return [];
        }

        var sorted = TargetSelector.OrderActions(yieldActions, _config.Monetize.OrderBy, ctx.Field, _cc);

        // インサイトプールの一定割合を確保
        var reserve = (long)(insightPool * _config.Monetize.ReserveRatio);
        var distributable = insightPool - reserve;

        var dists = new List<MonetizeDistribution>();
        var remaining = distributable;

        foreach (var a in sorted)
        {
            if (remaining <= 0)
            {
                break;
            }
            var amount = Math.Min(a.RemainingCapacity, remaining);
            if (amount > 0)
            {
                dists.Add(new MonetizeDistribution
                {
                    InstanceID = a.SourceInstanceID!,
                    Amount = amount,
                });
                remaining -= amount;
            }
        }

        if (!dists.Any())
        {
            return [];
        }

        return
        [
            new NpcAction
            {
                ActionType = ActionTypes.Monetize,
                Data = new MonetizeRequest { Distributions = dists },
            }
        ];
    }

    // ═══════════════════════════════════════════════════════════════
    //  Attack target selection
    // ═══════════════════════════════════════════════════════════════

    private string ResolveAttackTarget(
        List<string>? validTargets, Field selfField, Field oppField)
    {
        if (!(validTargets?.Count > 0))
        {
            throw new InvalidOperationException("Attack action has no valid targets");
        }

        var spec = _config.TargetSelection.Attack
            ?? throw new InvalidOperationException(
                $"No attack target selection configured in model '{_config.Model}'");

        return TargetSelector.ResolveFromValid(spec, validTargets, selfField, oppField, _cc)
            ?? throw new InvalidOperationException("No valid attack target found on opponent field");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Game phase overlay
    // ═══════════════════════════════════════════════════════════════

    private AiConfig ResolveActiveConfig(DecisionContext ctx)
    {
        var late = _config.GamePhases?.Late;
        if (late is null || !GuardChecker.CheckPhaseCondition(late.Condition, ctx, _cc))
        {
            return _config;
        }

        // レイトフェーズのオーバーライドをベース設定にマージ
        return new AiConfig
        {
            Model = _config.Model,
            Faction = _config.Faction,
            Deck = _config.Deck,
            Budget = _config.Budget,
            GamePhases = _config.GamePhases,
            Deploy = _config.Deploy,
            ImmediateCards = _config.ImmediateCards,
            EffectPriorities = MergeEffectPriorities(
                _config.EffectPriorities, late.EffectPriorities),
            TargetSelection = MergeTargetSelection(_config.TargetSelection, late.TargetSelection),
            ScaleUp = _config.ScaleUp,
            Monetize = _config.Monetize,
            Attachments = _config.Attachments,
            Reactive = _config.Reactive,
            SlotSelect = _config.SlotSelect,
        };
    }

    private static TargetSelectionConfig MergeTargetSelection(
        TargetSelectionConfig baseConfig, TargetSelectionConfig? overlay)
    {
        if (overlay is null)
        {
            return baseConfig;
        }
        return new TargetSelectionConfig
        {
            Attack = overlay.Attack ?? baseConfig.Attack,
            SingleDamage = overlay.SingleDamage ?? baseConfig.SingleDamage,
            Debuff = overlay.Debuff ?? baseConfig.Debuff,
            Buff = overlay.Buff ?? baseConfig.Buff,
            Heal = overlay.Heal ?? baseConfig.Heal,
        };
    }

    private static Dictionary<string, EffectPriorityEntry> MergeEffectPriorities(
        Dictionary<string, EffectPriorityEntry> basePri,
        Dictionary<string, EffectPriorityEntry>? overrides)
    {
        if (overrides is null)
        {
            return basePri;
        }

        var merged = new Dictionary<string, EffectPriorityEntry>(basePri);
        foreach (var (key, val) in overrides)
        {
            merged[key] = val;
        }
        return merged;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    private static NpcAction MakeEndPhaseAction() =>
        new() { ActionType = ActionTypes.EndPhase };

    private static bool MatchesCardType(CardDefinition card, string typeKey)
    {
        return typeKey switch
        {
            "compute" => card.IsComputeType,
            "data" => card.IsDataType,
            "platform" => card.CardType == CardTypes.Platform,
            "attachment" => card.CardType == CardTypes.Attachment,
            _ => card.CardType == typeKey,
        };
    }

    private CardDefinition ResolveCard(string cardId)
    {
        return _cc.Get(cardId)
            ?? throw new InvalidOperationException($"Card '{cardId}' not found in card cache");
    }

    private long TotalFieldMaintenanceCost(Field field)
    {
        return FieldHelpers.AllResources(field)
            .Sum(r => ResolveCard(r.CardID).MaintenanceCost);
    }

    private bool WouldExceedMaintenanceLimit(DecisionContext ctx, long additionalCost)
    {
        var current = TotalFieldMaintenanceCost(ctx.Field);
        var limit = (long)(_config.Budget.MaintenanceLimitRatio * ctx.Budget);
        return current + additionalCost > limit;
    }
}
