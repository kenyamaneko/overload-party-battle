using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// Evaluates NPC card effects using category-based priority scoring.
/// Pure static methods — no state.
/// </summary>
public static class ActionEvaluator
{
    /// <summary>
    /// Checks if a card's effect should be used and returns (priority, shouldUse, choiceData).
    /// </summary>
    public static (int Priority, bool Use, Dictionary<string, object>? ChoiceData) EvaluateCard(
        string cardId, TriggerType trigger, DecisionContext ctx,
        IEffectRegistry effects, ICardCache cc)
    {
        var info = effects.GetEffectInfo(cardId, trigger);
        if (info is null)
        {
            return (0, false, null);
        }

        // Check conditions (RequireBudget, RequireFactionCount, etc.)
        if (!CheckConditions(info.Conditions, ctx, cc))
        {
            return (0, false, null);
        }

        // Evaluate categories — use the highest priority among all
        var evaluated = info.Categories
            .Select(cat => EvaluateCategory(cat, info, ctx))
            .Where(r => r.Use)
            .ToList();

        if (evaluated.Count == 0)
        {
            return (0, false, null);
        }

        int maxPri = evaluated.Max(r => r.Priority);

        // Target selection
        Dictionary<string, object>? choiceData = null;
        if (info.TargetType == EffectTargetType.Choice)
        {
            var target = SelectTarget(info, ctx, cc);
            if (target is null)
            {
                return (0, false, null); // No valid target
            }
            choiceData = new Dictionary<string, object> { ["instanceId"] = target };
        }

        return (maxPri, true, choiceData);
    }

    /// <summary>
    /// Returns (priority, shouldUse) for a single effect category.
    /// </summary>
    public static (int Priority, bool Use) EvaluateCategory(
        EffectCategory cat, EffectInfo info, DecisionContext ctx)
    {
        switch (cat)
        {
            case EffectCategory.BudgetGain:
                return ctx.Budget < NpcParams.LowBudgetThreshold
                    ? (NpcParams.PriBudgetGainHigh, true)
                    : (NpcParams.PriBudgetGainLow, true);

            case EffectCategory.BudgetPenalty:
                return (NpcParams.PriBudgetPenalty, true);

            case EffectCategory.InsightGain:
                return (NpcParams.PriInsightGain, true);

            case EffectCategory.InsightAbsorb:
                return TargetSelector.CountAllResources(ctx.OppField) > 0
                    ? (NpcParams.PriInsightAbsorb, true)
                    : (0, false);

            case EffectCategory.Draw:
                return ctx.Hand.Count <= 3
                    ? (NpcParams.PriDrawHigh, true)
                    : (NpcParams.PriDrawLow, true);

            case EffectCategory.Search:
                return ctx.Hand.Count <= 3
                    ? (NpcParams.PriSearchHigh, true)
                    : (NpcParams.PriSearchLow, true);

            case EffectCategory.AoEDamage:
            {
                var count = TargetSelector.CountResourcesInZone(ctx.OppField, info.TargetZone);
                return count >= 2 ? (NpcParams.PriAoEDamage, true) : (0, false);
            }

            case EffectCategory.SingleDamage:
            {
                var count = TargetSelector.CountResourcesInZone(ctx.OppField, info.TargetZone);
                return count > 0 ? (NpcParams.PriSingleDamage, true) : (0, false);
            }

            case EffectCategory.Debuff:
                return TargetSelector.CountAllResources(ctx.OppField) > 0
                    ? (NpcParams.PriDebuff, true)
                    : (0, false);

            case EffectCategory.Buff:
                return TargetSelector.CountAllResources(ctx.Field) > 0
                    ? (NpcParams.PriBuff, true)
                    : (0, false);

            case EffectCategory.Heal:
                return TargetSelector.HasDamagedResource(ctx.Field)
                    ? (NpcParams.PriHeal, true)
                    : (0, false);

            case EffectCategory.DeployFree:
                return (NpcParams.PriDeployFree, true);

            case EffectCategory.RecoverCard:
                return (NpcParams.PriRecoverCard, true);

            case EffectCategory.RevealReactive:
                return TargetSelector.HasFaceDownSupport(ctx.OppField)
                    ? (NpcParams.PriRevealReactive, true)
                    : (0, false);

            case EffectCategory.DestroyPlatform:
                return TargetSelector.HasPlatform(ctx.OppField, ctx.Ai.CardCache)
                    ? (NpcParams.PriDestroyPlatform, true)
                    : (0, false);

            case EffectCategory.CancelAction:
                // Reactive — don't proactively use
                return (0, false);

            case EffectCategory.Survive:
                // Reactive — don't proactively use
                return (0, false);

            default:
                return (0, false);
        }
    }

    /// <summary>
    /// Verifies all EffectConditions are met.
    /// </summary>
    public static bool CheckConditions(
        List<EffectCondition> conditions, DecisionContext ctx, ICardCache cc)
    {
        return conditions.All(cond => cond.Type switch
        {
            ConditionTypes.MinBudget => ctx.Budget >= cond.Value,
            ConditionTypes.MaxBudget => ctx.Budget <= cond.Value,
            ConditionTypes.FactionCount => CountFactionOnField(ctx.Field, cond.Faction!, cc) >= cond.Value,
            ConditionTypes.OpponentBackend => HasFaceUpBackend(ctx.OppField),
            _ => true,
        });
    }

    /// <summary>
    /// Picks an appropriate target based on effect categories.
    /// </summary>
    public static string? SelectTarget(EffectInfo info, DecisionContext ctx, ICardCache cc)
    {
        var zone = info.TargetZone;

        // Damage targets → weakest opponent resource (easiest to destroy)
        if (info.HasCategory(EffectCategory.SingleDamage))
        {
            return TargetSelector.WeakestInZone(ctx.OppField, zone);
        }

        // Debuff targets → strongest opponent resource (most impactful debuff)
        if (info.HasCategory(EffectCategory.Debuff))
        {
            return TargetSelector.StrongestInZone(ctx.OppField, zone, cc);
        }

        // Heal targets → most damaged own resource
        if (info.HasCategory(EffectCategory.Heal))
        {
            return TargetSelector.MostDamagedOwn(ctx.Field);
        }

        // Buff targets → strongest own resource (maximize value)
        if (info.HasCategory(EffectCategory.Buff))
        {
            return TargetSelector.StrongestInZone(ctx.Field, zone, cc);
        }

        // Destroy platform → first platform
        if (info.HasCategory(EffectCategory.DestroyPlatform))
        {
            return TargetSelector.FirstPlatformId(ctx.OppField, cc);
        }

        // Fallback: weakest opponent resource
        return TargetSelector.WeakestInZone(ctx.OppField, zone);
    }

    // ─── Private helpers ────────────────────────────────────────

    private static int CountFactionOnField(Field field, string faction, ICardCache cc)
    {
        return FieldHelpers.AllFaceUpResources(field)
            .Count(r => cc.Get(r.CardID)?.Faction == faction);
    }

    private static bool HasFaceUpBackend(Field field)
    {
        return field.Backend.Any(r => r.FaceUp);
    }
}
