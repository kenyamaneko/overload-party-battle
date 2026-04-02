using System.Text.Json;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// Named custom effect implementations that cannot be expressed with standard ops.
/// Implements ICustomEffectRegistry to support parameterized customs via YAML meta.
/// </summary>
public class CustomEffectRegistry : ICustomEffectRegistry
{
    private readonly Dictionary<string, Func<Dictionary<string, JsonElement>?, Action<OpContext>?>> _factories = new()
    {
        // Phase 2-2: existing customs (migrated from EffectInit)
        ["chain_attack_bonus"] = _ => ChainAttackBonus,
        ["deploy_same_type_from_hand"] = _ => DeploySameTypeFromHand,
        ["disable_high_tp_deploy"] = _ => DisableHighTpDeploy,
        ["cancel_nth_deploy"] = _ => CancelNthDeploy,
        ["redirect_attack"] = _ => RedirectAttack,

        // Phase 2-3: new customs
        ["cloud_shift"] = BuildCloudShift,
        ["halve_incident_damage"] = _ => HalveIncidentDamage,
        ["spot_expiry"] = BuildSpotExpiry,
        ["reattach"] = _ => Reattach,

        ["scale_to_zero"] = _ => ScaleToZero,
    };

    /// <inheritdoc />
    public Action<OpContext>? Build(string customName, Dictionary<string, JsonElement>? meta)
    {
        if (!_factories.TryGetValue(customName, out var factory))
        {
            return null;
        }
        return factory(meta);
    }

    // ================================================================
    // Phase 2-2: existing customs
    // ================================================================

    /// <summary>
    /// If another Sugar Compute is on own frontend, deal 200 bonus damage to target.
    /// </summary>
    public static void ChainAttackBonus(OpContext octx)
    {
        if (octx.Target is null)
        {
            return;
        }

        var ally = octx.MyField.Frontend
            .Where(r => r.InstanceID != octx.Source?.InstanceID)
            .FirstOrDefault(r =>
            {
                var card = octx.CardCache.Get(r.CardID);
                return card is not null && card.Faction == Factions.Sugar && card.IsComputeType;
            });
        if (ally is not null)
        {
            octx.Target.Damage += 200;
        }
    }

    /// <summary>
    /// Validate choice card type matches destroyed target's type, request slot selection for deploy.
    /// </summary>
    public static void DeploySameTypeFromHand(OpContext octx)
    {
        if (octx.Target is null)
        {
            throw new GameRuleException("No target");
        }

        string? choiceCardId = octx.ChoiceData?.GetValueOrDefault("cardId")?.ToString();
        if (choiceCardId is null)
        {
            throw new GameRuleException("No card chosen");
        }

        var targetCard = octx.CardCache.Get(octx.Target.CardID);
        var choiceCard = octx.CardCache.Get(choiceCardId);
        if (targetCard is null || choiceCard is null)
        {
            throw new GameRuleException("Card not found");
        }
        if (choiceCard.CardType != targetCard.CardType)
        {
            throw new GameRuleException("Must deploy same type as destroyed card");
        }

        SlotRequestHelpers.DeployFromHand(octx, choiceCardId);
    }

    /// <summary>
    /// When opponent deploys a Compute/AI_ML card with TP >= 900, apply cannot_operate.
    /// </summary>
    public static void DisableHighTpDeploy(OpContext octx)
    {
        var target = octx.Target;
        if (target is null)
        {
            return;
        }

        var targetCard = octx.CardCache.Get(target.CardID);
        if (targetCard is null || (!targetCard.IsComputeType && targetCard.CardType != CardTypes.AiMl))
        {
            return;
        }
        if (target.MaxTP is null || target.MaxTP < 900)
        {
            return;
        }

        target.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = EffectTypes.CannotOperate,
            Value = 1,
            Duration = "this_turn",
            SourceID = "rate_limiter",
        });

        long deployerNum = octx.State.OpponentOf(octx.PlayerNum);
        var field = octx.GetField(deployerNum);
        var found = FieldHelpers.FindResourceByID(field, target.InstanceID);
        if (found is not null)
        {
            found.TemporaryEffects = target.TemporaryEffects;
        }
    }

    /// <summary>
    /// When opponent deploys their 3rd resource in a turn, cancel (destroy) it.
    /// </summary>
    public static void CancelNthDeploy(OpContext octx)
    {
        if (octx.SupSource is { EffectUsedThisTurn: true })
        {
            throw new GameRuleException("Already used this turn");
        }

        long deployerNum = octx.State.OpponentOf(octx.PlayerNum);
        var field = octx.GetField(deployerNum);
        int count = FieldHelpers.AllResources(field).Count(r => r.DeployedOnTurn == octx.State.CurrentTurn);
        if (count != 3)
        {
            throw new GameRuleException($"Not the 3rd deploy (count={count})");
        }

        if (octx.SupSource is not null)
        {
            octx.SupSource.EffectUsedThisTurn = true;
        }

        octx.CancelAction();
    }

    /// <summary>
    /// Validate redirect target is opponent's frontend.
    /// </summary>
    public static void RedirectAttack(OpContext octx)
    {
        var instanceId = octx.ChoiceData?.GetValueOrDefault("instanceId")?.ToString();
        if (instanceId is null)
        {
            throw new GameRuleException("No redirect target chosen");
        }

        var oppField = octx.OpponentField;
        if (FieldHelpers.FindResourceZone(oppField, instanceId) != Zone.Frontend)
        {
            throw new GameRuleException("Redirect target must be opponent's frontend");
        }
    }

    // ================================================================
    // Phase 2-3: new customs
    // ================================================================

    /// <summary>
    /// Deploy a resource from hand matching faction/card_type filter, then self-destruct the source.
    /// meta: { faction, card_type, deploy_discount }
    /// </summary>
    private static Action<OpContext>? BuildCloudShift(Dictionary<string, JsonElement>? meta)
    {
        if (meta is null)
        {
            return null;
        }

        string? faction = meta.TryGetValue("faction", out var fc) ? fc.GetString() : null;
        var cardTypes = meta.TryGetValue("card_type", out var ct)
            ? ct.EnumerateArray().Select(e => e.GetString()!).ToList()
            : null;
        long discount = meta.TryGetValue("deploy_discount", out var dc) ? dc.GetInt64() : 0;

        return octx =>
        {
            string? choiceCardId = octx.ChoiceData?.GetValueOrDefault("cardId")?.ToString();
            if (choiceCardId is null)
            {
                throw new GameRuleException("No card chosen for cloud_shift");
            }

            var card = octx.CardCache.Get(choiceCardId);
            if (card is null)
            {
                throw new GameRuleException($"Card {choiceCardId} not found");
            }
            if (faction is not null && card.Faction != faction)
            {
                throw new GameRuleException($"Card must be {faction} faction");
            }
            if (cardTypes is { Count: > 0 } && !cardTypes.Contains(card.CardType))
            {
                throw new GameRuleException($"Card type {card.CardType} not allowed");
            }

            if (discount > 0)
            {
                long budget = octx.State.GetBudget(octx.PlayerNum);
                octx.State.SetBudget(octx.PlayerNum, budget + discount);
            }

            SlotRequestHelpers.DeployFromHand(octx, choiceCardId);

            if (octx.SupSource is not null)
            {
                FieldHelpers.DestroySupport(octx.State, octx.PlayerNum, octx.MyField, octx.SupSource.InstanceID);
            }
        };
    }

    /// <summary>
    /// Halve incident damage by applying an incident_halve buff to all own face-up resources.
    /// IncidentDamageOp checks for this buff and halves the effective damage.
    /// </summary>
    public static void HalveIncidentDamage(OpContext octx)
    {
        var field = octx.MyField;
        foreach (var r in FieldHelpers.AllFaceUpResources(field))
        {
            r.TemporaryEffects.Add(new TemporaryEffect
            {
                EffectType = "incident_halve",
                Value = 1,
                Duration = "this_turn",
                SourceID = "halve_incident",
            });
        }
    }

    /// <summary>
    /// Self-destruct after N turns since deploy.
    /// meta: { turns }
    /// </summary>
    private static Action<OpContext>? BuildSpotExpiry(Dictionary<string, JsonElement>? meta)
    {
        int expiryTurns = meta is not null && meta.TryGetValue("turns", out var tEl)
            ? tEl.GetInt32()
            : 2;

        return octx =>
        {
            if (octx.Source is null)
            {
                return;
            }

            long deployedOn = octx.Source.DeployedOnTurn;
            long currentTurn = octx.State.CurrentTurn;

            if (deployedOn > 0 && currentTurn - deployedOn >= expiryTurns)
            {
                var field = octx.MyField;
                ResourceHelpers.DestroyResource(octx.State, octx.PlayerNum, field, octx.Source, octx.CardCache);
            }
        };
    }

    /// <summary>
    /// Move this attachment to a different valid target resource.
    /// </summary>
    public static void Reattach(OpContext octx)
    {
        var newTargetId = octx.ChoiceData?.GetValueOrDefault("instanceId")?.ToString();
        if (newTargetId is null)
        {
            throw new GameRuleException("No target chosen for reattach");
        }

        if (octx.SupSource is null)
        {
            throw new GameRuleException("Source is not a support card");
        }

        var field = octx.MyField;
        var newTarget = FieldHelpers.FindResourceByID(field, newTargetId);
        if (newTarget is null)
        {
            throw new GameRuleException("Target resource not found");
        }

        string attachmentId = octx.SupSource.InstanceID;
        string cardId = octx.SupSource.CardID;

        foreach (var r in FieldHelpers.AllResources(field))
        {
            var existing = r.Attachments.FirstOrDefault(a => a.InstanceID == attachmentId);
            if (existing is not null)
            {
                r.Attachments.Remove(existing);
                break;
            }
        }

        newTarget.Attachments.Add(new AttachmentRef
        {
            InstanceID = attachmentId,
            CardID = cardId,
        });
    }

    /// <summary>
    /// Scale to Zero: if the source did not attack last turn and was not deployed this turn,
    /// set its maintenance cost to 0 for this turn.
    /// </summary>
    public static void ScaleToZero(OpContext octx)
    {
        if (octx.Source is null)
        {
            return;
        }

        if (octx.Source.DeployedOnTurn == octx.State.CurrentTurn)
        {
            throw new GameRuleException("Cannot use on deploy turn");
        }

        if (octx.Source.LastAttackTurn >= octx.State.CurrentTurn - 1)
        {
            throw new GameRuleException("Source attacked last turn");
        }

        // Elastic カードの維持費を算出して同額の reduction を付与
        var card = octx.CardCache.MustGet(octx.Source.CardID);
        long intrinsic = card.IsComputeType ? card.BaseThroughput : card.BaseYield;
        long scaledStat = intrinsic * BattleConstants.RankMultiplier(octx.Source.Rank) + octx.Source.ElasticBonus;
        long maintenanceCost = Math.Max(0, scaledStat - card.FreeTier) * card.CostPerRequest / 100;

        if (maintenanceCost <= 0) { return; }

        octx.Source.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = "maintenance_reduction",
            Value = maintenanceCost,
            Duration = "this_turn",
            SourceID = "scale_to_zero",
        });
    }
}
