namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// A composable operation in an effect pipeline.
/// Each Op is an atomic unit that modifies game state via OpContext.
/// </summary>
public interface IEffectOp
{
    /// <summary>
    /// Execute this operation. ops 内で投げた GameRuleException は composer が
    /// 握り潰さず caller (UseExceptionHandler) まで伝搬する (A-1 不正入力 / A-2 内部不変条件違反)。
    /// passive trigger 経路で「発動条件不成立」を表すときは throw せず
    /// ctx.Result.GuardFailed = true をセットして return する。
    /// </summary>
    /// <param name="ctx">パイプライン実行コンテキスト。</param>
    void Execute(OpContext ctx);
}
