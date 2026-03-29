namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// Composes a sequence of IEffectOp into an EffectHandler.
/// Creates a shared OpContext and runs ops in sequence,
/// catching GameRuleException as guard/precondition failures.
/// </summary>
public static class EffectComposer
{
    /// <summary>
    /// Composes an array of ops into a single effect handler.
    /// </summary>
    /// <param name="ops">The ops to compose.</param>
    /// <returns>An <see cref="EffectHandler"/> that executes the ops in sequence.</returns>
    public static EffectHandler Compose(params IEffectOp[] ops)
    {
        var opsCopy = ops.ToArray();
        return ctx => RunOps(opsCopy, ctx);
    }

    /// <summary>
    /// Composes a list of ops into a single effect handler.
    /// </summary>
    /// <param name="ops">The ops to compose.</param>
    /// <returns>An <see cref="EffectHandler"/> that executes the ops in sequence.</returns>
    public static EffectHandler Compose(List<IEffectOp> ops)
    {
        var opsCopy = ops.ToArray();
        return ctx => RunOps(opsCopy, ctx);
    }

    private static EffectResult RunOps(IEffectOp[] ops, EffectContext ctx)
    {
        var octx = new OpContext(ctx);
        try
        {
            foreach (var op in ops)
            {
                op.Execute(octx);
            }
        }
        catch (GameRuleException)
        {
            octx.Result.GuardFailed = true;
        }
        return octx.Result;
    }
}
