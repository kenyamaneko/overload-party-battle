namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// A composable operation in an effect pipeline.
/// Each Op is an atomic unit that modifies game state via OpContext.
/// </summary>
public interface IEffectOp
{
    /// <summary>
    /// Execute this operation. Throw GameRuleException to abort the pipeline (guard/condition failed).
    /// </summary>
    void Execute(OpContext ctx);
}
