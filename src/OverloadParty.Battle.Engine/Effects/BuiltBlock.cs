using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// loader が組み立てた 1 効果ブロック。guard 述語列と ops パイプラインを分離して保持する。
/// </summary>
public class BuiltBlock
{
    /// <summary>発動条件述語列。すべて true を返したときだけ ops を実行する。</summary>
    public IEffectGuard[] Guards { get; init; } = [];

    /// <summary>guard 通過後に順次実行する ops。</summary>
    public IEffectOp[] Ops { get; init; } = [];
}

/// <summary>
/// 効果トリガーが active (= プレイヤー手動発動 = active) か passive (= イベント自動発動 = passive) かの判別。
/// guard 不成立時の扱い (active = 例外 / passive = 空の結果) を切り替えるために使う。
/// </summary>
public static class TriggerActivation
{
    /// <summary>
    /// 指定 trigger が active かを返します。Ignition のみ active で、他は passive。
    /// </summary>
    /// <param name="trigger">判別対象の trigger。</param>
    /// <returns>active なら true。</returns>
    public static bool IsActive(TriggerType trigger) =>
        trigger == TriggerType.Ignition;
}
