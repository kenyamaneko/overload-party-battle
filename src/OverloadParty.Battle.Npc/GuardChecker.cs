using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// Evaluates condition definitions from AI config against the current game state.
/// Uses the same vocabulary as Effect YAML guards (stat, count).
/// </summary>
public static class GuardChecker
{
    public static bool Check(ConditionDef? cond, DecisionContext ctx, ICardCache cc)
    {
        if (cond is null)
        {
            return true;
        }

        // stat condition (budget, damage)
        if (cond.Stat is not null)
        {
            return CheckStat(cond, ctx);
        }

        // count condition (resource count with selector)
        if (cond.Selector is not null)
        {
            return CheckCount(cond, ctx, cc);
        }

        return true;
    }

    public static bool CheckAll(List<ConditionDef>? conditions, DecisionContext ctx, ICardCache cc)
    {
        if (conditions is null || conditions.Count == 0)
        {
            return true;
        }

        return conditions.All(c => Check(c, ctx, cc));
    }

    public static bool CheckPhaseCondition(PhaseCondition cond, DecisionContext ctx, ICardCache cc)
    {
        if (cond.TurnMin is not null && ctx.CurrentTurn < cond.TurnMin.Value)
        {
            return false;
        }

        if (cond.Count is not null && !Check(cond.Count, ctx, cc))
        {
            return false;
        }

        return true;
    }

    // ─── Private ────────────────────────────────────────────────

    private static bool CheckStat(ConditionDef cond, DecisionContext ctx)
    {
        long value = cond.Stat switch
        {
            "budget" => ctx.Budget,
            "damage" => AggregateDamage(ctx),
            var s => throw new InvalidOperationException($"Unknown stat type: '{s}'"),
        };

        if (cond.Min is not null && value < cond.Min.Value)
        {
            return false;
        }
        if (cond.Max is not null && value > cond.Max.Value)
        {
            return false;
        }

        return true;
    }

    private static bool CheckCount(ConditionDef cond, DecisionContext ctx, ICardCache cc)
    {
        var count = CountResources(cond.Selector!, ctx, cc);

        if (cond.Min is not null && count < cond.Min.Value)
        {
            return false;
        }
        if (cond.Max is not null && count > cond.Max.Value)
        {
            return false;
        }

        return true;
    }

    private static int CountResources(SelectorDef sel, DecisionContext ctx, ICardCache cc)
    {
        var field = sel.Owner switch
        {
            "self" => ctx.Field,
            "opponent" => ctx.OppField,
            var o => throw new InvalidOperationException($"Unknown selector owner: '{o}'"),
        };

        // サポートゾーン face-down counting (for reactive detection)
        if (sel.Owner == "opponent" && sel.FaceDown == true && sel.Zone == "support")
        {
            return field.Support.Count(s => !s.FaceUp);
        }

        return TargetSelector.FilterResources(field, sel, cc).Count();
    }

    private static long AggregateDamage(DecisionContext ctx)
    {
        return FieldHelpers.AllFaceUpResources(ctx.Field).Sum(r => r.Damage);
    }
}
