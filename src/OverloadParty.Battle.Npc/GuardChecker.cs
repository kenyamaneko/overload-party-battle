using OverloadParty.Battle.Engine;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// Evaluates condition definitions from AI config against the current game state.
/// Uses the same vocabulary as Effect YAML guards (stat, count).
/// </summary>
public static class GuardChecker
{
    /// <summary>
    /// 単一条件の成立を判定します。
    /// </summary>
    /// <param name="cond">判定対象の条件定義。null は常に成立扱い。</param>
    /// <param name="ctx">意思決定コンテキスト。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <returns>条件が成立するなら true。</returns>
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

    /// <summary>
    /// 全条件の同時成立を判定します。
    /// </summary>
    /// <param name="conditions">判定対象の条件定義列。null / 空は常に成立扱い。</param>
    /// <param name="ctx">意思決定コンテキスト。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <returns>全条件が成立するなら true。</returns>
    public static bool CheckAll(List<ConditionDef>? conditions, DecisionContext ctx, ICardCache cc)
    {
        if (conditions is null || conditions.Count == 0)
        {
            return true;
        }

        return conditions.All(c => Check(c, ctx, cc));
    }

    /// <summary>
    /// ゲーム進行フェーズ条件 (ターン数下限 + 個数条件) の成立を判定します。
    /// </summary>
    /// <param name="cond">判定対象のフェーズ条件。</param>
    /// <param name="ctx">意思決定コンテキスト。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <returns>フェーズ条件が成立するなら true。</returns>
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
        // 相手サポートゾーンの裏向きカード数 (リアクティブ検出用)。
        // wire の HiddenDeployedSupport は FaceDown フラグだけが見える。
        if (sel.Owner == "opponent" && sel.FaceDown == true && sel.Zone == "support")
        {
            return WireFieldHelpers.AllSupports(ctx.OppField).Count(s => s.FaceDown);
        }

        return TargetSelector.FilterResources(sel, ctx.Field, ctx.OppField, cc).Count();
    }

    private static long AggregateDamage(DecisionContext ctx) =>
        WireFieldHelpers.AllFaceUpResources(ctx.Field).Sum(r => r.Damage);
}
