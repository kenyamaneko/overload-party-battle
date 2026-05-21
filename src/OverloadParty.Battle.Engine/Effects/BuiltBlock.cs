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
