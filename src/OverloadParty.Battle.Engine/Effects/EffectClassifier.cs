using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// EffectCategory は NPC 意思決定用に効果の動作カテゴリを定義します
/// </summary>
public enum EffectCategory
{
    BudgetGain,
    BudgetPenalty,
    InsightAbsorb,
    InsightGain,
    SingleDamage,
    AoEDamage,
    Buff,
    Debuff,
    Heal,
    Draw,
    Search,
    DeployFree,
    RecoverCard,
    RevealReactive,
    DestroyPlatform,
    CancelAction,
    Survive,
    SelfDestruct,
    CostReduction,
    Defensive,
    Utility,
}

/// <summary>
/// EffectTargetType は NPC が効果のターゲットを選択する方法を定義します
/// </summary>
public enum EffectTargetType
{
    None,
    Choice,
    AllOpp,
    Myself,
}

/// <summary>
/// EffectCondition は効果発動の前提条件を表現します
/// </summary>
public class EffectCondition
{
    /// <summary>Condition type identifier (see <see cref="ConditionTypes"/>).</summary>
    public string Type { get; init; } = "";

    /// <summary>Numeric threshold for budget conditions (min_budget / max_budget).</summary>
    public long Value { get; init; }

    // ─── resource_count 条件用 (ResourceCountGuard の filter shape を運ぶ) ──

    /// <summary>Owner filter for resource_count condition ("myself" / "opponent" / "both").</summary>
    public string? Owner { get; init; }

    /// <summary>Zone filter for resource_count condition (frontend / backend / support).</summary>
    public string? Zone { get; init; }

    /// <summary>Faction filter for resource_count condition.</summary>
    public string? Faction { get; init; }

    /// <summary>Card type filter for resource_count condition.</summary>
    public IReadOnlyList<string>? CardTypes { get; init; }

    /// <summary>Card ID filter for resource_count condition.</summary>
    public IReadOnlyList<string>? CardIds { get; init; }

    /// <summary>Minimum count for resource_count condition.</summary>
    public int? Min { get; init; }

    /// <summary>Maximum count for resource_count condition.</summary>
    public int? Max { get; init; }
}

/// <summary>
/// EffectInfo は NPC AI 用の効果パイプライン分類結果を保持します
/// </summary>
public class EffectInfo
{
    /// <summary>Categories describing what this effect does.</summary>
    public List<EffectCategory> Categories { get; } = [];

    /// <summary>How the NPC should select targets for this effect.</summary>
    public EffectTargetType TargetType { get; set; } = EffectTargetType.None;

    /// <summary>Zone the effect targets, if applicable.</summary>
    public string? TargetZone { get; set; }

    /// <summary>Prerequisites that must be met for the effect to fire.</summary>
    public List<EffectCondition> Conditions { get; } = [];

    /// <summary>Whether the effect contains a branching choice.</summary>
    public bool HasBranch { get; set; }

    /// <summary>
    /// Checks whether this effect has the given category.
    /// </summary>
    /// <param name="cat">Category to check.</param>
    /// <returns>True if the category is present.</returns>
    public bool HasCategory(EffectCategory cat) => Categories.Contains(cat);

    /// <summary>カテゴリを未登録ならリストに追加します。</summary>
    /// <param name="cat">追加するカテゴリ。</param>
    internal void AddCategory(EffectCategory cat)
    {
        if (!HasCategory(cat))
        {
            Categories.Add(cat);
        }
    }

    /// <summary>別の <see cref="EffectInfo"/> のカテゴリとターゲット情報を取り込みます。</summary>
    /// <param name="other">取り込み元の分類結果。</param>
    internal void MergeCategories(EffectInfo other)
    {
        foreach (var cat in other.Categories)
        {
            AddCategory(cat);
        }
        if (TargetType == EffectTargetType.None)
        {
            TargetType = other.TargetType;
            TargetZone = other.TargetZone;
        }
    }
}

/// <summary>
/// EffectClassifier は NPC 意思決定用に Op パイプラインを検査して効果を分類します
/// </summary>
public static class EffectClassifier
{
    /// <summary>
    /// guards / ops を NPC 意思決定用の <see cref="EffectInfo"/> に分類します。
    /// </summary>
    /// <param name="block">分類対象のブロック。</param>
    /// <returns>効果の振る舞いを表す分類結果。</returns>
    public static EffectInfo ClassifyBlock(BuiltBlock block)
    {
        var info = new EffectInfo();
        foreach (var guard in block.Guards)
        {
            ClassifyGuard(info, guard);
        }
        foreach (var op in block.Ops)
        {
            ClassifyOp(info, op);
        }
        return info;
    }

    /// <summary>
    /// ops のみを分類します (テスト向け / 旧 API)。
    /// </summary>
    /// <param name="ops">分類対象の ops 列。</param>
    /// <returns>効果の振る舞いを表す分類結果。</returns>
    public static EffectInfo ClassifyOps(IEffectOp[] ops)
    {
        var info = new EffectInfo();
        foreach (var op in ops)
        {
            ClassifyOp(info, op);
        }
        return info;
    }

    private static void ClassifyGuard(EffectInfo info, IEffectGuard guard)
    {
        switch (guard)
        {
            case MinBudgetGuard min:
                info.Conditions.Add(new EffectCondition { Type = ConditionTypes.MinBudget, Value = min.Min });
                break;
            case MaxBudgetGuard max:
                info.Conditions.Add(new EffectCondition { Type = ConditionTypes.MaxBudget, Value = max.Max });
                break;
            case ResourceCountGuard rcg:
                info.Conditions.Add(new EffectCondition
                {
                    Type = ConditionTypes.ResourceCount,
                    Owner = rcg.Owner,
                    Zone = rcg.Zone,
                    Faction = rcg.Faction,
                    CardTypes = rcg.CardTypes,
                    CardIds = rcg.CardIds,
                    Min = rcg.Min,
                    Max = rcg.Max,
                });
                break;
        }
    }

    private static void ClassifyOp(EffectInfo info, IEffectOp op)
    {
        switch (op)
        {
            // バジェット
            case GainBudgetOp g:
                info.AddCategory(EffectCategory.BudgetGain);
                break;
            case LoseBudgetOp:
                info.AddCategory(EffectCategory.BudgetPenalty);
                break;

            // インサイト
            case GainInsightOp:
                info.AddCategory(EffectCategory.InsightGain);
                break;
            case AbsorbInsightOp:
                info.AddCategory(EffectCategory.InsightAbsorb);
                break;

            // ダメージ
            case DealDamageOp d:
                ClassifyDamageTarget(info, d);
                break;
            case IncidentDamageOp id:
                ClassifyIncidentDamageTarget(info, id);
                break;

            // バフ / デバフ
            case ApplyBuffOp b:
                ClassifyBuffTarget(info, b);
                break;

            // ヒール
            case HealDamageOp:
            case FullHealOp:
                info.AddCategory(EffectCategory.Heal);
                break;

            // カード移動
            case DrawCardsOp:
                info.AddCategory(EffectCategory.Draw);
                break;
            case SearchRepoOp:
                info.AddCategory(EffectCategory.Search);
                break;
            case DeployFromHandOp:
                info.AddCategory(EffectCategory.DeployFree);
                break;
            case DeployFromRepoOp:
                info.AddCategory(EffectCategory.DeployFree);
                break;
            case DeployFromRepoSameCardOp:
                info.AddCategory(EffectCategory.DeployFree);
                break;
            case AddToHandOp:
                info.AddCategory(EffectCategory.RecoverCard);
                break;
            case TrashToHandOp:
                info.AddCategory(EffectCategory.RecoverCard);
                break;

            // フィールド
            case RevealReactiveOp:
                info.AddCategory(EffectCategory.RevealReactive);
                break;
            case DestroyPlatformOp:
                info.AddCategory(EffectCategory.DestroyPlatform);
                break;

            // リアクティブ
            case SetCancelActionOp:
                info.AddCategory(EffectCategory.CancelAction);
                break;
            case SurviveDestructionOp:
                info.AddCategory(EffectCategory.Survive);
                break;

            // 分岐
            case BranchOnChoiceOp bc:
                info.HasBranch = true;
                foreach (var branchOps in bc.Branches.Values)
                {
                    var inner = ClassifyOps(branchOps.ToArray());
                    info.MergeCategories(inner);
                }
                break;
            case IfConditionOp ic:
                var thenInfo = ClassifyOps(ic.Then.ToArray());
                info.MergeCategories(thenInfo);
                break;

            // タグ付きカスタム関数
            case CustomFnTaggedOp tagged:
                foreach (var cat in tagged.Categories)
                {
                    info.AddCategory(cat);
                }
                if (tagged.Target != EffectTargetType.None)
                {
                    info.TargetType = tagged.Target;
                }
                if (tagged.Zone is not null)
                {
                    info.TargetZone = tagged.Zone;
                }
                break;
        }
    }

    private static void ClassifyDamageTarget(EffectInfo info, DealDamageOp op)
    {
        // Use reflection-free approach by checking selector type
        ClassifyDamageSelector(info, op);
    }

    private static void ClassifyIncidentDamageTarget(EffectInfo info, IncidentDamageOp op)
    {
        ClassifyDamageSelector(info, op);
    }

    private static void ClassifyDamageSelector(EffectInfo info, IEffectOp op)
    {
        // We need to check the selector type — get it via pattern matching on known ops
        ISelector? sel = op switch
        {
            DealDamageOp d => GetSelector(d),
            IncidentDamageOp i => GetSelector(i),
            _ => null,
        };

        switch (sel)
        {
            case ByChoiceSelector bcs:
                info.AddCategory(EffectCategory.SingleDamage);
                info.TargetType = EffectTargetType.Choice;
                info.TargetZone = bcs.Zone;
                break;
            case AllOpponentSelector:
                info.AddCategory(EffectCategory.AoEDamage);
                info.TargetType = EffectTargetType.AllOpp;
                break;
            case AllOwnSelector:
                info.AddCategory(EffectCategory.AoEDamage);
                info.TargetType = EffectTargetType.Myself;
                break;
            case SourceSelector:
                info.TargetType = EffectTargetType.Myself;
                break;
            default:
                info.AddCategory(EffectCategory.SingleDamage);
                break;
        }
    }

    private static void ClassifyBuffTarget(EffectInfo info, ApplyBuffOp op)
    {
        var sel = GetSelector(op);
        switch (sel)
        {
            case SourceSelector:
            case AllOwnSelector:
                info.AddCategory(EffectCategory.Buff);
                info.TargetType = EffectTargetType.Myself;
                break;
            case ByChoiceSelector bcs:
                info.AddCategory(bcs.Owner == "opponent" ? EffectCategory.Debuff : EffectCategory.Buff);
                info.TargetType = EffectTargetType.Choice;
                info.TargetZone = bcs.Zone;
                break;
            case AllOpponentSelector:
                info.AddCategory(EffectCategory.Debuff);
                info.TargetType = EffectTargetType.AllOpp;
                break;
            default:
                info.AddCategory(EffectCategory.Buff);
                break;
        }
    }

    // Helper to extract selector from ops using reflection-free approach.
    // Ops store their selector in the constructor, so we access them via well-known fields.
    private static ISelector? GetSelector(object op)
    {
        // Use a simple field accessor approach since we control all the op types
        return op switch
        {
            DealDamageOp d => d.Selector,
            IncidentDamageOp i => i.Selector,
            HealDamageOp h => h.Selector,
            FullHealOp f => f.Selector,
            ApplyBuffOp b => b.Selector,
            _ => null,
        };
    }
}
