using System.Linq;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// Default rule-based NPC strategy.
/// Priority: immediate cards → deploy resources → activate effects → scale up → monetize → end phase.
/// </summary>
public class StandardAi : INpcStrategy
{
    internal readonly ICardCache CardCache;
    internal readonly IEffectRegistry Effects;

    public StandardAi(ICardCache cardCache, IEffectRegistry effects)
    {
        CardCache = cardCache;
        Effects = effects;
    }

    public virtual List<NpcAction> DecideMainPhaseActions(
        GameState state, Game game, long npcPlayerNum, List<AvailableAction> available)
    {
        var field = state.GetField(npcPlayerNum);
        var oppField = state.GetField(state.OpponentOf(npcPlayerNum));
        var hand = state.GetHand(npcPlayerNum);
        var budget = state.GetBudget(npcPlayerNum);

        var ctx = new DecisionContext(field, oppField, hand, budget, this);
        var usedZones = new HashSet<string>();
        var actions = new List<NpcAction>();

        // 1. Use Strategy/Incident cards (evaluated by effect category)
        actions.AddRange(DoImmediateActions(ctx, available, usedZones));

        // 2. Deploy resource cards
        actions.AddRange(DoDeployActions(ctx, available, usedZones));

        // 3. Activate field resource/support effects
        actions.AddRange(DecideActivateActions(ctx, available));

        // 4. Scale up existing resources
        actions.AddRange(DoScaleUpActions(available, GetInstanceFamily()));

        // 5. Monetize
        var insightPool = state.GetInsightPool(npcPlayerNum);
        if (insightPool > 0)
        {
            actions.AddRange(DoMonetizeActions(available, insightPool));
        }

        // 6. End phase
        actions.Add(MakeEndPhaseAction());
        return actions;
    }

    public virtual List<NpcAction> DecideBattlePhaseActions(
        GameState state, Game game, long npcPlayerNum, List<AvailableAction> available)
    {
        var oppField = state.GetField(state.OpponentOf(npcPlayerNum));
        return DoBattleActions(available, oppField);
    }

    public virtual List<string> DecideDiscard(GameState state, long npcPlayerNum)
    {
        var hand = state.GetHand(npcPlayerNum);
        var discardCount = hand.Count - GameConstants.HandLimit;
        if (discardCount <= 0)
        {
            return [];
        }

        // Sort by maintenance cost ascending — discard cheapest cards first
        var values = new List<(string InstanceID, long Cost)>();
        foreach (var hc in hand)
        {
            var card = CardCache.Get(hc.CardID);
            var cost = card?.MaintenanceCost ?? 0;
            values.Add((hc.InstanceID, cost));
        }
        values.Sort((a, b) => a.Cost.CompareTo(b.Cost));

        var ids = new List<string>();
        for (int i = 0; i < discardCount && i < values.Count; i++)
        {
            ids.Add(values[i].InstanceID);
        }
        return ids;
    }

    protected virtual string GetInstanceFamily() => "M";

    // ─── Immediate (Strategy/Incident) actions ─────────────────

    protected List<NpcAction> DoImmediateActions(
        DecisionContext ctx, List<AvailableAction> available, HashSet<string> usedZones)
    {
        var playActions = ActionFilter.FilterByType(available, WireActionTypes.PlayCard);

        var candidates = new List<(AvailableAction Action, CardDefinition Card, int Priority, Dictionary<string, object>? Choice)>();
        foreach (var a in playActions)
        {
            var card = CardCache.Get(a.CardID);
            if (card is null || !FieldHelpers.IsImmediateType(card.CardType))
            {
                continue;
            }

            var (pri, use, choice) = ActionEvaluator.EvaluateCard(
                card.CardNo, TriggerType.Activate, ctx, Effects, CardCache);
            if (!use)
            {
                continue;
            }

            candidates.Add((a, card, pri, choice));
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

    // ─── Deploy actions ─────────────────────────────────────────

    protected List<NpcAction> DoDeployActions(
        DecisionContext ctx, List<AvailableAction> available, HashSet<string> usedZones)
    {
        var playActions = ActionFilter.FilterByType(available, WireActionTypes.PlayCard);

        var candidates = new List<(AvailableAction Action, CardDefinition Card, int Priority)>();
        foreach (var a in playActions)
        {
            var card = CardCache.Get(a.CardID);
            if (card is null)
            {
                continue;
            }
            if (FieldHelpers.IsImmediateType(card.CardType) || card.CardType == CardTypes.Attachment)
            {
                continue;
            }

            int pri = card.IsComputeType ? 0 : card.IsDataType ? 1 : 2;
            candidates.Add((a, card, pri));
        }
        candidates.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        var actions = new List<NpcAction>();
        var deployed = new HashSet<string>();
        foreach (var c in candidates)
        {
            if (deployed.Contains(c.Action.HandInstanceID!))
            {
                continue;
            }

            var zone = ActionFilter.PickBestZone(c.Action.ValidZones, c.Card, usedZones);
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
                var choice = DeployChoiceFor(c.Card.CardNo);
                if (choice == "")
                {
                    choice = c.Action.ChoiceOptions.First();
                }
                payload["choiceData"] = new Dictionary<string, string> { ["option"] = choice };
            }

            actions.Add(new NpcAction { ActionType = WireActionTypes.PlayCard, Data = payload });
            deployed.Add(c.Action.HandInstanceID!);
            usedZones.Add(zone);
        }
        return actions;
    }

    // ─── Activate effect actions ────────────────────────────────

    protected List<NpcAction> DecideActivateActions(DecisionContext ctx, List<AvailableAction> available)
    {
        var activateActions = ActionFilter.FilterByType(available, WireActionTypes.ActivateEffect);

        var candidates = new List<(AvailableAction Action, long CardNo, int Priority, Dictionary<string, object>? Choice)>();
        foreach (var a in activateActions)
        {
            var cardNo = ActionFilter.ResolveCardNoForInstance(a.SourceInstanceID!, ctx.Field);
            if (cardNo == 0)
            {
                continue;
            }

            var (pri, use, choice) = ActionEvaluator.EvaluateCard(
                cardNo, TriggerType.Activate, ctx, Effects, CardCache);
            if (!use)
            {
                continue;
            }

            // If effect needs target choice but evaluateCard didn't provide one,
            // select from the pre-validated ValidTargets
            if (a.EffectTargetType == "Choice" && choice is null)
            {
                if (!(a.ValidTargets?.Count > 0))
                {
                    continue;
                }
                var target = SelectTargetFromValid(cardNo, a.ValidTargets, ctx);
                if (target is null)
                {
                    continue;
                }
                choice = new Dictionary<string, object> { ["instanceId"] = target };
            }

            candidates.Add((a, cardNo, pri, choice));
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

            actions.Add(new NpcAction { ActionType = WireActionTypes.ActivateEffect, Data = payload });
        }
        return actions;
    }

    private string? SelectTargetFromValid(long cardNo, List<string> validTargets, DecisionContext ctx)
    {
        if (!validTargets.Any())
        {
            return null;
        }

        var validSet = new HashSet<string>(validTargets);

        // Try using heuristic-based target selection
        var info = Effects.GetEffectInfo(cardNo, TriggerType.Activate);
        if (info is not null)
        {
            var target = ActionEvaluator.SelectTarget(info, ctx, CardCache);
            if (target is not null && validSet.Contains(target))
            {
                return target;
            }
        }

        // Fallback: first valid target
        return validTargets.FirstOrDefault();
    }

    // ─── Battle actions ─────────────────────────────────────────

    protected static List<NpcAction> DoBattleActions(List<AvailableAction> available, Field oppField)
    {
        var attackActions = ActionFilter.FilterByType(available, WireActionTypes.Attack);
        var actions = new List<NpcAction>();

        foreach (var a in attackActions)
        {
            var target = ActionFilter.FindBestTargetFromValid(a.ValidTargets, oppField);
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

    // ─── Scale up actions ───────────────────────────────────────

    protected static List<NpcAction> DoScaleUpActions(List<AvailableAction> available, string family)
    {
        var scaleActions = ActionFilter.FilterByType(available, WireActionTypes.ScaleUp);
        var actions = new List<NpcAction>();

        foreach (var a in scaleActions)
        {
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
        }
        return actions;
    }

    // ─── Monetize actions ───────────────────────────────

    protected static List<NpcAction> DoMonetizeActions(List<AvailableAction> available, long insightPool)
    {
        var yieldActions = ActionFilter.FilterByType(available, WireActionTypes.Monetize);
        if (!yieldActions.Any())
        {
            return [];
        }

        var dists = new List<Dictionary<string, object>>();
        var remaining = insightPool;

        foreach (var a in yieldActions)
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

    // ─── Helpers ────────────────────────────────────────────────

    protected static NpcAction MakeEndPhaseAction() =>
        new() { ActionType = WireActionTypes.EndPhase, Data = new Dictionary<string, object>() };

    protected static string DeployChoiceFor(long cardNo) => cardNo switch
    {
        7 => "use",       // SD RDB - アデリース: 予約契約 — "use" saves budget long-term
        11 => "redis",    // SD Cache - メリー: Memcached (instant) vs Redis (permanent Yield)
        125 => "redis",   // Sugar Lab Cache - メレンゲもりもりストア
        _ => "",
    };
}

/// <summary>
/// Context holding all state needed for NPC category-based decisions.
/// </summary>
public class DecisionContext(Field field, Field oppField, List<HandCard> hand, long budget, StandardAi ai)
{
    public Field Field { get; } = field;
    public Field OppField { get; } = oppField;
    public List<HandCard> Hand { get; } = hand;
    public long Budget { get; } = budget;
    public StandardAi Ai { get; } = ai;
}
