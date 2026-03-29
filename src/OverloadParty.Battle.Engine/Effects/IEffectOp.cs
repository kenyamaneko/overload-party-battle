namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// A composable operation in an effect pipeline.
/// Each Op is an atomic unit that modifies game state via OpContext.
/// </summary>
public interface IEffectOp
{
    /// <summary>
    /// Execute this operation.
    /// ガード条件が満たされない場合は GameRuleException を投げてパイプラインを中断する。
    /// EffectComposer がキャッチして EffectResult.GuardFailed = true に変換する。
    /// </summary>
    void Execute(OpContext ctx);
}
