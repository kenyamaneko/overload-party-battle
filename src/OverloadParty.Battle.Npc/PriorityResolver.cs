using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// Resolves effect category priorities from AiConfig instead of hardcoded NpcParams.
/// </summary>
public static class PriorityResolver
{
    private static readonly Dictionary<EffectCategory, string> CategoryKeys = new()
    {
        [EffectCategory.BudgetGain] = EffectCategories.BudgetGain,
        [EffectCategory.BudgetPenalty] = EffectCategories.BudgetPenalty,
        [EffectCategory.InsightGain] = EffectCategories.InsightGain,
        [EffectCategory.InsightAbsorb] = EffectCategories.InsightAbsorb,
        [EffectCategory.Draw] = EffectCategories.Draw,
        [EffectCategory.Search] = EffectCategories.Search,
        [EffectCategory.AoEDamage] = EffectCategories.AoeDamage,
        [EffectCategory.SingleDamage] = EffectCategories.SingleDamage,
        [EffectCategory.Debuff] = EffectCategories.Debuff,
        [EffectCategory.Buff] = EffectCategories.Buff,
        [EffectCategory.Heal] = EffectCategories.Heal,
        [EffectCategory.DeployFree] = EffectCategories.DeployFree,
        [EffectCategory.RecoverCard] = EffectCategories.RecoverCard,
        [EffectCategory.RevealReactive] = EffectCategories.RevealReactive,
        [EffectCategory.DestroyPlatform] = EffectCategories.DestroyPlatform,
        [EffectCategory.CancelAction] = EffectCategories.CancelAction,
        [EffectCategory.Survive] = EffectCategories.Survive,
    };

    public static string? CategoryToKey(EffectCategory cat)
    {
        return CategoryKeys.GetValueOrDefault(cat);
    }

    /// <summary>
    /// Evaluates a card's effect and returns (priority, shouldUse, choiceData).
    /// </summary>
    public static (int Priority, bool Use, Dictionary<string, object>? ChoiceData) Evaluate(
        string cardId, TriggerType trigger, DecisionContext ctx,
        AiConfig config, IEffectRegistry effects, ICardCache cc)
    {
        var info = effects.GetEffectInfo(cardId, trigger);
        if (info is null)
        {
            return (0, false, null);
        }

        if (!CheckEffectConditions(info.Conditions, ctx, cc))
        {
            return (0, false, null);
        }

        var evaluated = info.Categories
            .Select(cat => Resolve(cat, info, ctx, config, cc))
            .Where(r => r.Use)
            .ToList();

        if (evaluated.Count == 0)
        {
            return (0, false, null);
        }

        int maxPri = evaluated.Max(r => r.Priority);

        Dictionary<string, object>? choiceData = null;
        if (info.TargetType == EffectTargetType.Choice)
        {
            var target = SelectTarget(info, ctx, config.TargetSelection, cc);
            if (target is null)
            {
                return (0, false, null);
            }
            choiceData = new Dictionary<string, object> { ["instanceId"] = target };
        }

        return (maxPri, true, choiceData);
    }

    /// <summary>
    /// Resolves priority for a single effect category based on config.
    /// </summary>
    public static (int Priority, bool Use) Resolve(
        EffectCategory cat, EffectInfo info, DecisionContext ctx,
        AiConfig config, ICardCache cc)
    {
        // Reactive categories — never use proactively
        if (cat is EffectCategory.CancelAction or EffectCategory.Survive)
        {
            return (0, false);
        }

        if (!CategoryKeys.TryGetValue(cat, out var key))
        {
            throw new InvalidOperationException($"No priority key for EffectCategory {cat}");
        }

        if (!config.EffectPriorities.TryGetValue(key, out var entry))
        {
            return (0, false);
        }

        // Check entry-level condition
        if (entry.Condition is not null && !GuardChecker.Check(entry.Condition, ctx, cc))
        {
            return (0, false);
        }

        // Category-specific feasibility checks
        if (!IsFeasible(cat, info, ctx, cc))
        {
            return (0, false);
        }

        // Resolve priority with thresholds
        int priority = ResolvePriority(cat, entry, ctx);

        return (priority, true);
    }

    // ─── Private ────────────────────────────────────────────────

    private static bool IsFeasible(EffectCategory cat, EffectInfo info, DecisionContext ctx, ICardCache cc)
    {
        return cat switch
        {
            EffectCategory.BudgetGain => true,
            EffectCategory.BudgetPenalty => true,
            EffectCategory.InsightGain => true,
            EffectCategory.Draw => true,
            EffectCategory.Search => true,
            EffectCategory.DeployFree => true,
            EffectCategory.RecoverCard => true,
            EffectCategory.InsightAbsorb => TargetSelector.CountAllResources(ctx.OppField) > 0,
            EffectCategory.AoEDamage => TargetSelector.CountResourcesInZone(ctx.OppField, info.TargetZone)
                                        >= 2,
            EffectCategory.SingleDamage => TargetSelector.CountResourcesInZone(ctx.OppField, info.TargetZone) > 0,
            EffectCategory.Debuff => TargetSelector.CountAllResources(ctx.OppField) > 0,
            EffectCategory.Buff => TargetSelector.CountAllResources(ctx.Field) > 0,
            EffectCategory.Heal => TargetSelector.HasDamagedResource(ctx.Field),
            EffectCategory.RevealReactive => TargetSelector.HasFaceDownSupport(ctx.OppField),
            EffectCategory.DestroyPlatform => TargetSelector.HasPlatform(ctx.OppField, cc),
            _ => throw new InvalidOperationException($"Unknown effect category: {cat}"),
        };
    }

    private static int ResolvePriority(EffectCategory cat, EffectPriorityEntry entry, DecisionContext ctx)
    {
        // Budget-based threshold (e.g. budget_gain: high when budget < threshold)
        if (entry.Threshold is not null && entry.LowPriority is not null)
        {
            return ctx.Budget < entry.Threshold.Value
                ? entry.Priority
                : entry.LowPriority.Value;
        }

        // Hand-based threshold (e.g. draw: high when hand <= threshold)
        if (entry.HandThreshold is not null && entry.LowPriority is not null)
        {
            return ctx.Hand.Count <= entry.HandThreshold.Value
                ? entry.Priority
                : entry.LowPriority.Value;
        }

        // AoE min_targets override (already checked in IsFeasible, but entry can raise the bar)
        // No additional logic needed — IsFeasible handles the minimum count

        return entry.Priority;
    }

    private static bool CheckEffectConditions(List<EffectCondition> conditions, DecisionContext ctx, ICardCache cc)
    {
        return conditions.All(cond => cond.Type switch
        {
            ConditionTypes.MinBudget => ctx.Budget >= cond.Value,
            ConditionTypes.MaxBudget => ctx.Budget <= cond.Value,
            ConditionTypes.FactionCount => CountFactionOnField(ctx.Field, cond.Faction!, cc) >= cond.Value,
            ConditionTypes.OpponentBackend => ctx.OppField.Backend.Any(r => r.FaceUp),
            var t => throw new InvalidOperationException($"Unknown effect condition type: '{t}'"),
        });
    }

    private static int CountFactionOnField(Field field, string faction, ICardCache cc)
    {
        return FieldHelpers.AllFaceUpResources(field)
            .Count(r =>
            {
                var card = cc.Get(r.CardID)
                    ?? throw new InvalidOperationException($"Card '{r.CardID}' not found in card cache");
                return card.Faction == faction;
            });
    }

    /// <summary>
    /// Target selection based on config TargetSpec definitions.
    /// </summary>
    public static string? SelectTarget(EffectInfo info, DecisionContext ctx, TargetSelectionConfig targets, ICardCache cc)
    {
        if (info.HasCategory(EffectCategory.SingleDamage) && targets.SingleDamage is not null)
        {
            return TargetSelector.Resolve(targets.SingleDamage, ctx.Field, ctx.OppField, cc);
        }

        if (info.HasCategory(EffectCategory.Debuff) && targets.Debuff is not null)
        {
            return TargetSelector.Resolve(targets.Debuff, ctx.Field, ctx.OppField, cc);
        }

        if (info.HasCategory(EffectCategory.Heal) && targets.Heal is not null)
        {
            return TargetSelector.Resolve(targets.Heal, ctx.Field, ctx.OppField, cc);
        }

        if (info.HasCategory(EffectCategory.Buff) && targets.Buff is not null)
        {
            return TargetSelector.Resolve(targets.Buff, ctx.Field, ctx.OppField, cc);
        }

        if (info.HasCategory(EffectCategory.DestroyPlatform))
        {
            return TargetSelector.FirstPlatformId(ctx.OppField, cc);
        }

        return targets.Attack is not null
            ? TargetSelector.Resolve(targets.Attack, ctx.Field, ctx.OppField, cc)
            : null;
    }
}
