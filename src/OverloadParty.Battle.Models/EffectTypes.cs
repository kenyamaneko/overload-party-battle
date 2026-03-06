namespace OverloadParty.Battle.Models;

/// <summary>
/// TemporaryEffect の EffectType 文字列定数。
///
/// buff_ と debuff_ を分けている理由:
/// - Value は常に正の数で管理し、加算(buff)か減算(debuff)かを EffectType で区別する
/// - エフェクト除去時に buff だけ / debuff だけを選択的にクリアできる
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
}
