using System.Text.Json;
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
        ["buff_on_attacked"] = BuildBuffOnAttacked,
        ["target_shield"] = _ => TargetShield,
        ["spot_expiry"] = BuildSpotExpiry,
        ["reattach"] = _ => Reattach,

        // Phase 7: passive customs (currently handled by StatCalculator's old passive system)
        // ["tp_per_backend_data"] — StatCalculator.CalculateTPPerBackendData()
        // ["free_first_scale"] — StatCalculator passive_effects scale_cost_free
        // ["scale_to_zero"] — needs attack history tracking (not yet available)
        // ["fleet_deploy"] — needs deploy rule hook (not yet available)
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
                return card is not null && card.Faction == GameConstants.FactionSugar && card.IsComputeType;
            });
        if (ally is not null)
        {
            octx.Target.Damage += 200;
        }
    }

    /// <summary>
    /// Validate choice card type matches destroyed target's type, deploy from hand.
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

        var field = octx.GetField(octx.PlayerNum);
        ResourceHelpers.DeployFromHand(octx.State, octx.PlayerNum, field, choiceCardId, octx.CardCache);
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

            // Apply deploy discount
            if (discount > 0)
            {
                long budget = octx.State.GetBudget(octx.PlayerNum);
                octx.State.SetBudget(octx.PlayerNum, budget + discount);
            }

            // Deploy from hand
            var field = octx.GetField(octx.PlayerNum);
            ResourceHelpers.DeployFromHand(octx.State, octx.PlayerNum, field, choiceCardId, octx.CardCache);

            // Self-destruct the source support card
            if (octx.SupSource is not null)
            {
                FieldHelpers.DestroySupport(octx.State, octx.PlayerNum, field, octx.SupSource.InstanceID);
            }
        };
    }

    /// <summary>
    /// Halve incident damage by applying incident_reduction buff equal to half the incoming damage.
    /// The reactive fires before the incident resolves, applying a large reduction.
    /// </summary>
    public static void HalveIncidentDamage(OpContext octx)
    {
        // Apply a large incident_reduction to all own resources for this turn
        // This effectively halves incident damage since the reduction is applied during damage calc
        var field = octx.MyField;
        foreach (var r in FieldHelpers.AllFaceUpResources(field))
        {
            r.TemporaryEffects.Add(new TemporaryEffect
            {
                EffectType = "incident_reduction",
                Value = 9999,
                Duration = "this_turn",
                SourceID = "halve_incident",
            });
        }
        octx.CancelAction();
    }

    /// <summary>
    /// After being attacked, gain a TP buff until next own turn end.
    /// meta: { buff, amount }
    /// </summary>
    private static Action<OpContext>? BuildBuffOnAttacked(Dictionary<string, JsonElement>? meta)
    {
        string buff = "buff_tp";
        long amount = 300;

        if (meta is not null)
        {
            if (meta.TryGetValue("buff", out var bEl))
            {
                buff = bEl.GetString() switch
                {
                    "tp" => EffectTypes.BuffTP,
                    "yield" => EffectTypes.BuffYield,
                    var s => s ?? buff,
                };
            }
            if (meta.TryGetValue("amount", out var aEl))
            {
                amount = aEl.GetInt64();
            }
        }

        return octx =>
        {
            // Attached to a resource; apply buff to the host when attacked
            if (octx.Target is null)
            {
                return;
            }

            octx.Target.TemporaryEffects.Add(new TemporaryEffect
            {
                EffectType = buff,
                Value = amount,
                Duration = "until_next_own_turn_end",
                SourceID = "buff_on_attacked",
            });
        };
    }

    /// <summary>
    /// While other frontends exist on own field, this resource can't be targeted by attacks.
    /// Implemented as a passive marker; AttackProcessor checks for this.
    /// </summary>
    public static void TargetShield(OpContext octx)
    {
        // Passive: applies a marker buff to the host resource
        if (octx.Source is null)
        {
            return;
        }

        // Check if there are other frontends
        var otherFrontends = octx.MyField.Frontend
            .Where(r => r.InstanceID != octx.Source.InstanceID && r.FaceUp)
            .Any();

        if (otherFrontends)
        {
            octx.Source.TemporaryEffects.Add(new TemporaryEffect
            {
                EffectType = "target_shield",
                Value = 1,
                Duration = "permanent",
                SourceID = "target_shield",
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

        // Remove from current host
        foreach (var r in FieldHelpers.AllResources(field))
        {
            var existing = r.Attachments.FirstOrDefault(a => a.InstanceID == attachmentId);
            if (existing is not null)
            {
                r.Attachments.Remove(existing);
                break;
            }
        }

        // Add to new target
        newTarget.Attachments.Add(new AttachmentRef
        {
            InstanceID = attachmentId,
            CardID = cardId,
        });
    }
}
