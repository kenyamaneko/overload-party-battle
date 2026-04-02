using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
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
        GameState state, Game game, long npcPlayerNum, List<AvailableAction> available)
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
        GameState state, Game game, long npcPlayerNum, List<AvailableAction> available)
    {
        var oppField = state.GetField(state.OpponentOf(npcPlayerNum));
        var attackActions = ActionFilter.FilterByType(available, WireActionTypes.Attack);
        var actions = new List<NpcAction>();

        foreach (var a in attackActions)
        {
            var target = ResolveAttackTarget(a.ValidTargets, oppField);
            if (target is null)
            {
                continue;
            }

            actions.Add(new NpcAction
            {
                ActionType = WireActionTypes.Attack,
                Data = new Dictionary<string, object>
                {
                    ["attackerInstanceId"] = a.SourceInstanceID!,
                    ["targetInstanceId"] = target,
                },
            });
        }

        actions.Add(MakeEndPhaseAction());
        return actions;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Discard
    // ═══════════════════════════════════════════════════════════════

    public List<string> DecideDiscard(GameState state, long npcPlayerNum, int discardCount)
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

    public NpcAction? DecideSlotSelect(GameState state, long npcPlayerNum)
    {
        var pending = state.PendingSlotSelects.FirstOrDefault();
        if (pending is null || pending.PlayerNum != npcPlayerNum || pending.ValidZones.Count == 0)
        {
            return null;
        }

        var zone = ActionFilter.ParseZoneStr(pending.ValidZones[0]);
        if (zone is null)
        {
            return null;
        }

        return new NpcAction
        {
            ActionType = WireActionTypes.SelectSlot,
            Data = new Dictionary<string, object>
            {
                ["zone"] = zone.Zone,
                ["index"] = zone.Index,
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
        var playActions = ActionFilter.FilterByType(available, WireActionTypes.PlayCard);

        var candidates = new List<(AvailableAction Action, int Priority, Dictionary<string, object>? Choice)>();
        foreach (var a in playActions)
        {
            var card = ResolveCard(a.CardID);
            if (!FieldHelpers.IsImmediateType(card.CardType))
            {
                continue;
            }

            // hold_until: don't use certain card types until condition is met
            if (ShouldHoldImmediate(card, ctx, activeConfig))
            {
                continue;
            }

            // use_conditions: check category-specific conditions
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

            var payload = new Dictionary<string, object>
            {
                ["cardInstanceId"] = c.Action.HandInstanceID!,
                ["position"] = ActionFilter.ParseZoneStr(zone)!,
            };
            if (c.Choice is not null)
            {
                payload["choiceData"] = c.Choice;
            }

            actions.Add(new NpcAction { ActionType = WireActionTypes.PlayCard, Data = payload });
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

        // Check effect categories for this card to find matching use_conditions
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
        var playActions = ActionFilter.FilterByType(available, WireActionTypes.PlayCard);

        var candidates = new List<(AvailableAction Action, CardDefinition Card, int Priority)>();
        foreach (var a in playActions)
        {
            var card = ResolveCard(a.CardID);
            if (FieldHelpers.IsImmediateType(card.CardType))
            {
                continue;
            }
            if (card.CardType == CardTypes.Attachment && _config.Attachments is not null)
            {
                continue;
            }
            // Reactive cards handled separately
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

            var payload = new Dictionary<string, object>
            {
                ["cardInstanceId"] = c.Action.HandInstanceID!,
                ["position"] = ActionFilter.ParseZoneStr(zone)!,
            };
            if (c.Action.ChoiceOptions?.Count > 0)
            {
                var choice = ResolveDeployChoice(c.Card.CardId);
                payload["choiceData"] = new Dictionary<string, string> { ["option"] = choice };
            }

            actions.Add(new NpcAction { ActionType = WireActionTypes.PlayCard, Data = payload });
            deployed.Add(c.Action.HandInstanceID!);
            usedZones.Add(zone);
            addedMaintenanceCost += c.Card.MaintenanceCost;
        }

        // Deploy attachments after resources
        if (_config.Attachments is not null)
        {
            actions.AddRange(DoAttachmentDeploy(ctx, playActions, usedZones));
        }

        // Deploy reactive cards
        if (_config.Reactive is not null)
        {
            actions.AddRange(DoReactiveDeploy(playActions, usedZones));
        }

        return actions;
    }

    private int ResolveDeployPriority(CardDefinition card, DecisionContext ctx)
    {
        // Check conditional priorities first (card-specific with conditions)
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

        // Check static priorities (card_id match first, then card_type)
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
            var zone = ActionFilter.PickSupportZone(c.Action.ValidZones, usedZones);
            if (zone is null)
            {
                continue;
            }

            var payload = new Dictionary<string, object>
            {
                ["cardInstanceId"] = c.Action.HandInstanceID!,
                ["position"] = ActionFilter.ParseZoneStr(zone)!,
            };

            actions.Add(new NpcAction { ActionType = WireActionTypes.PlayCard, Data = payload });
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

        // Count existing reactive cards in support zone
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

            var payload = new Dictionary<string, object>
            {
                ["cardInstanceId"] = c.Action.HandInstanceID!,
                ["position"] = ActionFilter.ParseZoneStr(zone)!,
            };

            actions.Add(new NpcAction { ActionType = WireActionTypes.PlayCard, Data = payload });
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
        var activateActions = ActionFilter.FilterByType(available, WireActionTypes.UseEffect);

        var candidates = new List<(AvailableAction Action, int Priority, Dictionary<string, object>? Choice)>();
        foreach (var a in activateActions)
        {
            var cardId = ActionFilter.ResolveCardIdForInstance(a.SourceInstanceID!, ctx.Field);
            if (cardId == "")
            {
                continue;
            }

            var (pri, use, choice) = PriorityResolver.Evaluate(
                cardId, TriggerType.Activate, ctx, activeConfig, _effects, _cc);
            if (!use)
            {
                continue;
            }

            // If effect needs target but evaluator didn't provide one, select from ValidTargets
            if (a.EffectTargetType == "Choice" && choice is null)
            {
                if (!(a.ValidTargets?.Count > 0))
                {
                    continue;
                }
                var target = SelectTargetFromValid(cardId, a.ValidTargets, ctx, activeConfig);
                if (target is null)
                {
                    continue;
                }
                choice = new Dictionary<string, object> { ["instanceId"] = target };
            }

            candidates.Add((a, pri, choice));
        }
        candidates.Sort((a, b) => b.Priority.CompareTo(a.Priority));

        var actions = new List<NpcAction>();
        foreach (var c in candidates)
        {
            var payload = new Dictionary<string, object>
            {
                ["instanceId"] = c.Action.SourceInstanceID!,
            };
            if (c.Choice is not null)
            {
                payload["choiceData"] = c.Choice;
            }

            actions.Add(new NpcAction { ActionType = WireActionTypes.UseEffect, Data = payload });
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
        var scaleActions = ActionFilter.FilterByType(available, WireActionTypes.ScaleUp);
        var family = ResolveInstanceFamily(ctx);

        // Sort by config priority
        var sorted = _config.ScaleUp.Priority switch
        {
            "highest_tp" => scaleActions
                .OrderByDescending(a => ResolveResourceValue(a.SourceInstanceID!, ctx.Field))
                .ToList(),
            var s => throw new InvalidOperationException(
                $"Unknown scale_up priority '{s}' in model '{_config.Model}'"),
        };

        var actions = new List<NpcAction>();
        var addedMaintenanceCost = 0L;

        foreach (var a in sorted)
        {
            var cardId = ActionFilter.ResolveCardIdForInstance(a.SourceInstanceID!, ctx.Field);
            var estimatedCostIncrease = cardId != ""
                ? ResolveCard(cardId).MaintenanceCost
                : 0L;
            if (estimatedCostIncrease > 0 &&
                WouldExceedMaintenanceLimit(ctx, addedMaintenanceCost + estimatedCostIncrease))
            {
                continue;
            }

            var payload = new Dictionary<string, object>
            {
                ["componentInstanceId"] = a.SourceInstanceID!,
                ["targetRank"] = a.TargetRank!,
            };
            if (a.NeedsFamily)
            {
                payload["instanceFamily"] = family;
            }

            actions.Add(new NpcAction { ActionType = WireActionTypes.ScaleUp, Data = payload });
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
        var yieldActions = ActionFilter.FilterByType(available, WireActionTypes.Monetize);
        if (!yieldActions.Any())
        {
            return [];
        }

        var sorted = _config.Monetize.Strategy switch
        {
            "highest_tp" => yieldActions.OrderByDescending(a =>
                ResolveResourceValue(a.SourceInstanceID!, ctx.Field)).ToList(),
            var s => throw new InvalidOperationException(
                $"Unknown monetize strategy '{s}' in model '{_config.Model}'"),
        };

        // Reserve a portion of insight pool
        var reserve = (long)(insightPool * _config.Monetize.ReserveRatio);
        var distributable = insightPool - reserve;

        var dists = new List<Dictionary<string, object>>();
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
                dists.Add(new Dictionary<string, object>
                {
                    ["componentInstanceId"] = a.SourceInstanceID!,
                    ["amount"] = amount,
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
                ActionType = WireActionTypes.Monetize,
                Data = new Dictionary<string, object> { ["distributions"] = dists },
            }
        ];
    }

    // ═══════════════════════════════════════════════════════════════
    //  Attack target selection
    // ═══════════════════════════════════════════════════════════════

    private string? ResolveAttackTarget(List<string>? validTargets, Field oppField)
    {
        if (!(validTargets?.Count > 0))
        {
            return null;
        }

        return _config.TargetSelection.Attack switch
        {
            "weakest_av" => ActionFilter.FindBestTargetFromValid(validTargets, oppField),
            "strongest_tp" => FindStrongestTarget(validTargets, oppField),
            var s => throw new InvalidOperationException(
                $"Unknown attack target strategy '{s}' in model '{_config.Model}'"),
        };
    }

    private string? FindStrongestTarget(List<string> validTargets, Field oppField)
    {
        var resMap = FieldHelpers.AllResources(oppField).ToDictionary(r => r.InstanceID);
        return validTargets
            .Where(id => resMap.ContainsKey(id))
            .OrderByDescending(id => TargetSelector.ResourceValue(resMap[id], _cc))
            .FirstOrDefault();
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

        // Merge late-phase overrides on top of base config
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
            TargetSelection = late.TargetSelection ?? _config.TargetSelection,
            ScaleUp = _config.ScaleUp,
            Monetize = _config.Monetize,
            Attachments = _config.Attachments,
            Reactive = _config.Reactive,
            SlotSelect = _config.SlotSelect,
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
        new() { ActionType = WireActionTypes.EndPhase, Data = new Dictionary<string, object>() };

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

    private long ResolveResourceValue(string instanceId, Field field)
    {
        var resource = FieldHelpers.AllResources(field)
            .FirstOrDefault(r => r.InstanceID == instanceId);
        if (resource is null)
        {
            return 0;
        }
        return TargetSelector.ResourceValue(resource, _cc);
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
