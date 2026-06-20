using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// PriorityResolver はハードコードされた NpcParams の代わりに AiConfig から効果カテゴリの優先度を解決します
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

    /// <summary>
    /// 効果カテゴリを設定ファイル上のキー文字列に変換します。
    /// </summary>
    /// <param name="cat">変換対象のカテゴリ。</param>
    /// <returns>対応するキー文字列。未登録なら null。</returns>
    public static string? MapCategoryToKey(EffectCategory cat)
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
        // リアクティブ / 自動発動 / 未分類カテゴリ — 能動的には使用しない
        if (cat is EffectCategory.CancelAction
                or EffectCategory.Survive
                or EffectCategory.SelfDestruct
                or EffectCategory.CostReduction
                or EffectCategory.Defensive
                or EffectCategory.Utility)
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

        if (entry.Condition is not null && !GuardChecker.Check(entry.Condition, ctx, cc))
        {
            return (0, false);
        }

        // Category-specific feasibility checks
        if (!IsFeasible(cat, info, ctx, cc))
        {
            return (0, false);
        }

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
        if (entry.Threshold is not null && entry.LowPriority is not null)
        {
            return ctx.Budget < entry.Threshold.Value
                ? entry.Priority
                : entry.LowPriority.Value;
        }

        if (entry.HandThreshold is not null && entry.LowPriority is not null)
        {
            return ctx.Hand.Count <= entry.HandThreshold.Value
                ? entry.Priority
                : entry.LowPriority.Value;
        }

        return entry.Priority;
    }

    private static bool CheckEffectConditions(List<EffectCondition> conditions, DecisionContext ctx, ICardCache cc)
    {
        return conditions.All(cond => cond.Type switch
        {
            ConditionTypes.MinBudget => ctx.Budget >= cond.Value,
            ConditionTypes.MaxBudget => ctx.Budget <= cond.Value,
            ConditionTypes.ResourceCount => CheckResourceCount(cond, ctx, cc),
            ConditionTypes.OpponentBackend => ctx.OppField.Backend.Any(r => r is not null && r.FaceUp),
            var t => throw new InvalidOperationException($"Unknown effect condition type: '{t}'"),
        });
    }

    private static bool CheckResourceCount(EffectCondition cond, DecisionContext ctx, ICardCache cc)
    {
        int count = 0;
        if (cond.Owner is "myself" or "both")
        {
            count += CountMatchingSelf(ctx.Field, cond, cc);
        }
        if (cond.Owner is "opponent" or "both")
        {
            count += CountMatchingOpp(ctx.OppField, cond, cc);
        }
        return (cond.Min is null || count >= cond.Min) && (cond.Max is null || count <= cond.Max);
    }

    private static int CountMatchingSelf(GD.Field field, EffectCondition cond, ICardCache cc) =>
        WireFieldHelpers.AllFaceUpResources(field)
            .Where(r => InZone(field.Frontend, field.Backend, r, cond.Zone))
            .Count(r => MatchesCardFilter(r, cond, cc));

    private static int CountMatchingOpp(GD.OpponentField field, EffectCondition cond, ICardCache cc) =>
        WireFieldHelpers.AllFaceUpResources(field)
            .Where(r => InZone(field.Frontend, field.Backend, r, cond.Zone))
            .Count(r => MatchesCardFilter(r, cond, cc));

    private static bool MatchesCardFilter(GD.DeployedResource r, EffectCondition cond, ICardCache cc)
    {
        var card = cc.Get(r.CardID)
            ?? throw new InvalidOperationException($"Card '{r.CardID}' not found in card cache");
        if (cond.Faction is not null && card.Faction != cond.Faction) { return false; }
        if (cond.CardTypes is { Count: > 0 } && !cond.CardTypes.Contains(card.CardType)) { return false; }
        if (cond.CardIds is { Count: > 0 } && !cond.CardIds.Contains(card.CardId)) { return false; }
        return true;
    }

    private static bool InZone(
        List<GD.DeployedResource?> frontend, List<GD.DeployedResource?> backend,
        GD.DeployedResource resource, string? zone) =>
        zone switch
        {
            null => true,
            "frontend" => frontend.Any(r => r?.InstanceID == resource.InstanceID),
            "backend" => backend.Any(r => r?.InstanceID == resource.InstanceID),
            _ => throw new InvalidOperationException($"Unknown zone filter: '{zone}'"),
        };

    /// <summary>
    /// Target selection based on config TargetSpec definitions.
    /// </summary>
    public static string? SelectTarget(EffectInfo info, DecisionContext ctx, TargetSelectionConfig targets, ICardCache cc)
    {
        if (info.HasCategory(EffectCategory.SingleDamage))
        {
            return ResolveCategoryTarget(EffectCategory.SingleDamage, targets.SingleDamage, ctx, cc);
        }

        if (info.HasCategory(EffectCategory.Debuff))
        {
            return ResolveCategoryTarget(EffectCategory.Debuff, targets.Debuff, ctx, cc);
        }

        if (info.HasCategory(EffectCategory.Heal))
        {
            return ResolveCategoryTarget(EffectCategory.Heal, targets.Heal, ctx, cc);
        }

        if (info.HasCategory(EffectCategory.Buff))
        {
            return ResolveCategoryTarget(EffectCategory.Buff, targets.Buff, ctx, cc);
        }

        if (info.HasCategory(EffectCategory.DestroyPlatform))
        {
            return TargetSelector.FindFirstPlatformId(ctx.OppField, cc);
        }

        throw new InvalidOperationException(
            $"SelectTarget: effect categories [{string.Join(", ", info.Categories)}] have no resolvable target spec");
    }

    private static string? ResolveCategoryTarget(
        EffectCategory category, TargetSpec? spec, DecisionContext ctx, ICardCache cc)
    {
        if (spec is null)
        {
            throw new InvalidOperationException(
                $"SelectTarget: category {category} has no target spec configured");
        }
        return TargetSelector.Resolve(spec, ctx.Field, ctx.OppField, cc);
    }
}
