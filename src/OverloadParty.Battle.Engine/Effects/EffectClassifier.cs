using OverloadParty.Battle.Engine.Effects.Ops;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// Categories describing what an effect does, for NPC decision-making.
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
    RevealTrap,
    DestroyPlatform,
    CancelAction,
    Survive,
}

/// <summary>
/// How the NPC should select targets for an effect.
/// </summary>
public enum EffectTargetType
{
    None,
    Choice,
    AllOpp,
    Self,
}

/// <summary>
/// A prerequisite for an effect to fire.
/// </summary>
public class EffectCondition
{
    public string Type { get; init; } = "";
    public long Value { get; init; }
    public string? Faction { get; init; }
}

/// <summary>
/// Classification result for an effect pipeline, used by NPC AI.
/// </summary>
public class EffectInfo
{
    public List<EffectCategory> Categories { get; } = [];
    public EffectTargetType TargetType { get; set; } = EffectTargetType.None;
    public string? TargetZone { get; set; }
    public List<EffectCondition> Conditions { get; } = [];
    public bool HasBranch { get; set; }

    public bool HasCategory(EffectCategory cat) => Categories.Contains(cat);

    internal void AddCategory(EffectCategory cat)
    {
        if (!HasCategory(cat)) Categories.Add(cat);
    }

    internal void MergeCategories(EffectInfo other)
    {
        foreach (var cat in other.Categories)
            AddCategory(cat);
        if (TargetType == EffectTargetType.None)
        {
            TargetType = other.TargetType;
            TargetZone = other.TargetZone;
        }
    }
}

/// <summary>
/// Inspects Op pipelines to classify effects for NPC decision-making.
/// </summary>
public static class EffectClassifier
{
    public static EffectInfo ClassifyOps(IEffectOp[] ops)
    {
        var info = new EffectInfo();
        foreach (var op in ops)
            ClassifyOp(info, op);
        return info;
    }

    private static void ClassifyOp(EffectInfo info, IEffectOp op)
    {
        switch (op)
        {
            // Budget
            case GainBudgetOp g:
                info.AddCategory(EffectCategory.BudgetGain);
                break;
            case LoseBudgetOp:
                info.AddCategory(EffectCategory.BudgetPenalty);
                break;

            // Insight
            case GainInsightOp:
                info.AddCategory(EffectCategory.InsightGain);
                break;
            case AbsorbInsightOp:
                info.AddCategory(EffectCategory.InsightAbsorb);
                break;

            // Damage
            case DealDamageOp d:
                ClassifyDamageTarget(info, d);
                break;
            case IncidentDamageOp id:
                ClassifyIncidentDamageTarget(info, id);
                break;

            // Buff / Debuff
            case ApplyBuffOp b:
                ClassifyBuffTarget(info, b);
                break;

            // Heal
            case HealDamageOp h:
                info.AddCategory(EffectCategory.Heal);
                break;

            // Card Movement
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

            // Field
            case RevealTrapOp:
                info.AddCategory(EffectCategory.RevealTrap);
                break;
            case DestroyPlatformOp:
                info.AddCategory(EffectCategory.DestroyPlatform);
                break;

            // Reactive
            case SetCancelActionOp:
                info.AddCategory(EffectCategory.CancelAction);
                break;
            case SurviveDestructionOp:
                info.AddCategory(EffectCategory.Survive);
                break;

            // Conditions
            case RequireBudgetOp rb:
                info.Conditions.Add(new EffectCondition { Type = "min_budget", Value = rb.Min });
                break;
            case RequireMaxBudgetOp rmb:
                info.Conditions.Add(new EffectCondition { Type = "max_budget", Value = rmb.Max });
                break;
            case RequireFactionCountOp rfc:
                info.Conditions.Add(new EffectCondition { Type = "faction_count", Value = rfc.Min, Faction = rfc.Faction });
                break;
            case RequireOpponentBackendOp:
                info.Conditions.Add(new EffectCondition { Type = "opponent_backend" });
                break;

            // Branching
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

            // CustomFn with tags
            case CustomFnTaggedOp tagged:
                foreach (var cat in tagged.Categories)
                    info.AddCategory(cat);
                if (tagged.Target != EffectTargetType.None)
                    info.TargetType = tagged.Target;
                if (tagged.Zone is not null)
                    info.TargetZone = tagged.Zone;
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
                info.TargetType = EffectTargetType.Self;
                break;
            case SourceSelector:
                info.TargetType = EffectTargetType.Self;
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
                info.TargetType = EffectTargetType.Self;
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
            ApplyBuffOp b => b.Selector,
            _ => null,
        };
    }
}
