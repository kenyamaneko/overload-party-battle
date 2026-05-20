using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// PriorityResolver はハードコードされた NpcParams の代わりに AiConfig からエフェクトカテゴリの優先度を解決します
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
    public static string? CategoryToKey(EffectCategory cat)
    {
        return CategoryKeys.GetValueOrDefault(cat);
    }

    /// <summary>
    /// Evaluates a card's effect and returns (priority, shouldUse, choiceData).
    /// </summary>
    /// <param name="cardId">評価対象のカード ID。</param>
    /// <param name="trigger">対象トリガー種別。</param>
    /// <param name="ctx">意思決定コンテキスト。</param>
    /// <param name="config">適用する AI 設定。</param>
    /// <param name="effects">効果情報の参照元。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <returns>優先度・使用可否・選択肢データのタプル。</returns>
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
    /// <param name="cat">評価対象の効果カテゴリ。</param>
    /// <param name="info">効果情報。</param>
    /// <param name="ctx">意思決定コンテキスト。</param>
    /// <param name="config">適用する AI 設定。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <returns>優先度と使用可否のタプル。</returns>
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
        // バジェット-based threshold (e.g. budget_gain: high when budget < threshold)
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
    /// <param name="info">対象効果の情報。</param>
    /// <param name="ctx">意思決定コンテキスト。</param>
    /// <param name="targets">ターゲット選択設定。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <returns>選択したターゲットの InstanceID。該当なしなら null。</returns>
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
            return TargetSelector.FirstPlatformId(ctx.OppField, cc);
        }

        throw new InvalidOperationException(
            $"SelectTarget: effect categories [{string.Join(", ", info.Categories)}] have no resolvable target spec");
    }

    /// <summary>
    /// Resolves a target via the category's configured TargetSpec.
    /// </summary>
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
