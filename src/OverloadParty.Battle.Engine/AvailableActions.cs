using System.Linq;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Represents a valid action a player can take.
/// </summary>
public class AvailableAction
{
    public string Type { get; set; } = "";
    public string? HandInstanceID { get; set; }
    public long CardID { get; set; }
    public List<string>? ValidZones { get; set; }
    public string? SourceInstanceID { get; set; }
    public List<string>? ValidTargets { get; set; }
    public string? TargetRank { get; set; }
    public bool NeedsFamily { get; set; }
    public long RemainingCapacity { get; set; }
    public string? EffectTargetType { get; set; }
    public int RequiredCount { get; set; }
    public List<string>? ChoiceOptions { get; set; }
}

/// <summary>
/// Turn control information for the UI.
/// </summary>
public class TurnControls
{
    public bool CanEndPhase { get; set; }
    public int DiscardRequired { get; set; }
}

/// <summary>
/// Computes available actions based on game state and phase.
/// </summary>
public static class AvailableActions
{
    public static TurnControls ComputeTurnControls(GameState state, List<HandCard> hand)
    {
        return new TurnControls
        {
            CanEndPhase = state.CurrentPhase is Phase.Main or Phase.Battle,
            DiscardRequired = state.CurrentPhase == Phase.End
                ? Math.Max(0, hand.Count - GameConstants.HandLimit)
                : 0,
        };
    }

    public static List<AvailableAction> Compute(
        GameState state, Game game, long playerNum,
        Field myField, Field oppField, List<HandCard> hand,
        long budget, long insightPool,
        ICardCache cc, IEffectRegistry? effects)
    {
        var actions = new List<AvailableAction>();

        switch (state.CurrentPhase)
        {
            case Phase.Main:
                actions.AddRange(EnumeratePlayCardActions(state, myField, hand, cc));
                actions.AddRange(EnumerateScaleUpActions(myField, cc));
                actions.AddRange(EnumerateDistributeYieldActions(state, myField, insightPool, cc));
                actions.AddRange(EnumerateActivateEffectActions(myField, oppField, cc, effects));
                actions.AddRange(EnumerateMigrateActions(myField, cc));
                break;

            case Phase.Battle:
                actions.AddRange(EnumerateAttackActions(myField, oppField, cc));
                actions.AddRange(EnumerateActivateEffectActions(myField, oppField, cc, effects));
                break;
        }

        return actions;
    }

    private static IEnumerable<AvailableAction> EnumeratePlayCardActions(
        GameState state, Field field, List<HandCard> hand, ICardCache cc)
    {
        foreach (var handCard in hand)
        {
            var card = cc.Get(handCard.CardID);
            if (card is null) continue;

            // Attachment cards: enumerate valid targets
            if (card.CardType == "Attachment")
            {
                var targets = new List<string>();
                foreach (var res in FieldHelpers.AllFaceUpResources(field))
                {
                    if (res.Attachments.Count < GameConstants.MaxAttachments)
                        targets.Add(res.InstanceID);
                }
                if (targets.Any())
                {
                    yield return new AvailableAction
                    {
                        Type = "play_card",
                        HandInstanceID = handCard.InstanceID,
                        CardID = handCard.CardID,
                        ValidTargets = targets,
                    };
                }
                continue;
            }

            // Incident: 1 per turn limit
            if (card.CardType == "Incident")
            {
                if (field.IncidentPlayedThisTurn) continue;
                // First player cannot use incidents on turn 1
                if (TurnManager.IsFirstTurn(state.CurrentTurn)) continue;
            }

            var validZones = new List<string>();

            // Frontend eligibility
            if (FieldHelpers.IsFrontendEligible(card.CardType))
                validZones.AddRange(field.Frontend.EmptySlotIndices().Select(i => $"frontend_{i}"));

            // Backend eligibility
            if (FieldHelpers.IsBackendEligible(card.CardType))
                validZones.AddRange(field.Backend.EmptySlotIndices().Select(i => $"backend_{i}"));

            // Support zone eligibility
            if (FieldHelpers.IsSupportType(card.CardType))
                validZones.AddRange(field.Support.EmptySlotIndices().Select(i => $"support_{i}"));

            if (validZones.Any())
            {
                yield return new AvailableAction
                {
                    Type = "play_card",
                    HandInstanceID = handCard.InstanceID,
                    CardID = handCard.CardID,
                    ValidZones = validZones,
                };
            }
        }
    }

    private static IEnumerable<AvailableAction> EnumerateAttackActions(
        Field myField, Field oppField, ICardCache cc)
    {
        // Determine valid targets
        bool oppHasFrontend = FieldHelpers.HasFrontendResources(oppField);
        var validTargets = new List<string>();

        var targetZone = oppHasFrontend ? oppField.Frontend : oppField.Backend;
        foreach (var res in targetZone.Where(r => r.FaceUp))
            validTargets.Add(res.InstanceID);

        if (!validTargets.Any()) yield break;

        // Find eligible attackers
        foreach (var attacker in myField.Frontend.Where(r => r.FaceUp))
        {
            var attackerCard = cc.Get(attacker.CardID);
            if (attackerCard is null || !attackerCard.IsComputeType) continue;
            if (attacker.HasAttacked) continue;
            if (FieldHelpers.HasTemporaryEffect(attacker, "cannot_operate")) continue;
            if (attacker.MigratingFrom is not null || attacker.MigrationTarget is not null) continue;

            yield return new AvailableAction
            {
                Type = "attack",
                SourceInstanceID = attacker.InstanceID,
                ValidTargets = validTargets,
            };
        }
    }

    private static IEnumerable<AvailableAction> EnumerateScaleUpActions(Field field, ICardCache cc)
    {
        foreach (var resource in FieldHelpers.AllFaceUpResources(field))
        {
            var card = cc.Get(resource.CardID);
            if (card is null || !card.Resizable) continue;
            if (card.Elastic) continue; // Elastic cards don't scale manually

            if (resource.Rank == Rank.Small)
            {
                yield return new AvailableAction
                {
                    Type = "scale_up",
                    SourceInstanceID = resource.InstanceID,
                    TargetRank = "medium",
                    NeedsFamily = true,
                };
            }
            else if (resource.Rank == Rank.Medium)
            {
                yield return new AvailableAction
                {
                    Type = "scale_up",
                    SourceInstanceID = resource.InstanceID,
                    TargetRank = "large",
                    NeedsFamily = false,
                };
            }
        }
    }

    private static IEnumerable<AvailableAction> EnumerateDistributeYieldActions(
        GameState state, Field field, long insightPool, ICardCache cc)
    {
        if (TurnManager.IsFirstTurn(state.CurrentTurn)) yield break;
        if (insightPool <= 0) yield break;

        foreach (var res in field.Backend.Where(r => r.FaceUp))
        {
            if (res.MigratingFrom is not null) continue;

            var card = cc.Get(res.CardID);
            if (card is null || !card.IsComputeType) continue;

            long effectiveTP = StatCalculator.CalculateEffectiveTP(res, field, cc);
            long remaining = effectiveTP - res.MonetizedAmount;
            if (remaining <= 0) continue;

            yield return new AvailableAction
            {
                Type = "distribute_yield",
                SourceInstanceID = res.InstanceID,
                RemainingCapacity = remaining,
            };
        }
    }

    private static IEnumerable<AvailableAction> EnumerateActivateEffectActions(
        Field myField, Field oppField, ICardCache cc, IEffectRegistry? effects)
    {
        if (effects is null) yield break;

        // Frontend and backend resources
        foreach (var resource in FieldHelpers.AllFaceUpResources(myField))
        {
            if (resource.EffectUsedThisTurn) continue;
            if (FieldHelpers.HasTemporaryEffect(resource, "cannot_operate")) continue;
            if (resource.MigratingFrom is not null || resource.MigrationTarget is not null) continue;

            var card = cc.Get(resource.CardID);
            if (card is null) continue;
            if (!effects.Has(card.CardNo, TriggerType.Activate)) continue;

            yield return new AvailableAction
            {
                Type = "activate_effect",
                SourceInstanceID = resource.InstanceID,
                CardID = card.CardNo,
            };
        }

        // Support zone
        foreach (var support in FieldHelpers.AllSupports(myField))
        {
            if (support.DeployingTurnsLeft > 0) continue;
            if (support.EffectUsedThisTurn) continue;

            var card = cc.Get(support.CardID);
            if (card is null) continue;
            if (!effects.Has(card.CardNo, TriggerType.Activate)) continue;

            yield return new AvailableAction
            {
                Type = "activate_effect",
                SourceInstanceID = support.InstanceID,
                CardID = card.CardNo,
            };
        }
    }

    private static IEnumerable<AvailableAction> EnumerateMigrateActions(Field field, ICardCache cc)
    {
        var sources = new List<(ResourceInstance Res, CardDefinition Card)>();
        var targets = new List<(ResourceInstance Res, CardDefinition Card)>();

        foreach (var resource in FieldHelpers.AllFaceUpResources(field))
        {
            if (resource.MigratingFrom is not null || resource.MigrationTarget is not null)
                continue;

            var card = cc.Get(resource.CardID);
            if (card is null) continue;

            sources.Add((resource, card));
            targets.Add((resource, card));
        }

        foreach (var (source, sourceCard) in sources)
        {
            var validTargets = new List<string>();
            foreach (var (target, targetCard) in targets)
            {
                if (target.InstanceID == source.InstanceID) continue;
                if (targetCard.DeployTurns < sourceCard.DeployTurns) continue;
                validTargets.Add(target.InstanceID);
            }

            if (validTargets.Any())
            {
                yield return new AvailableAction
                {
                    Type = "migrate",
                    SourceInstanceID = source.InstanceID,
                    ValidTargets = validTargets,
                };
            }
        }
    }
}
