namespace OverloadParty.Battle.Models;

/// <summary>
/// TemporaryEffect の EffectType 文字列定数。
///
/// buff_ と debuff_ を分けている理由:
/// - Value は常に正の数で管理し、加算(buff)か減算(debuff)かを EffectType で区別する
/// - 効果除去時に buff だけ / debuff だけを選択的にクリアできる
/// - NPC AI の判断で Buff / Debuff を明確に分類できる（EffectClassifier）
/// </summary>
public static class EffectTypes
{
    // ─── Buff (加算される一時効果) ──────────────────────────
    public const string BuffTP = "buff_tp";
    public const string BuffYield = "buff_yield";

    // ─── Debuff (減算される一時効果) ─────────────────────────
    public const string DebuffTP = "debuff_tp";
    public const string DebuffYield = "debuff_yield";

    // ─── 状態異常 ───────────────────────────────────────────
    public const string CannotOperate = "cannot_operate";
    public const string TPSuppressed = "tp_suppressed";
}

/// <summary>
/// NPC 効果評価で使う条件タイプの文字列定数。
/// EffectClassifier で条件を生成し、ActionEvaluator で判定する。
/// </summary>
public static class ConditionTypes
{
    public const string MinBudget = "min_budget";
    public const string MaxBudget = "max_budget";
    public const string ResourceCount = "resource_count";
    public const string OpponentBackend = "opponent_backend";
}
