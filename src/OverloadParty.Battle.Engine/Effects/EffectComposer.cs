namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// Composes a sequence of IEffectOp into an EffectHandler.
/// Creates a shared OpContext and runs ops in sequence,
/// catching GameRuleException as guard/precondition failures.
/// </summary>
public static class EffectComposer
{
    public static EffectHandler Compose(params IEffectOp[] ops)
    {
        // Capture a copy of the ops array
        var opsCopy = ops.ToArray();

        return ctx =>
        {
            var octx = new OpContext(ctx);
            foreach (var op in opsCopy)
            {
                op.Execute(octx);
            }
            return octx.Result;
        };
    }

    public static EffectHandler Compose(List<IEffectOp> ops)
    {
        var opsCopy = ops.ToList();

        return ctx =>
        {
            var octx = new OpContext(ctx);
            foreach (var op in opsCopy)
            {
                op.Execute(octx);
            }
            return octx.Result;
        };
    }
}
