using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// Applies a temporary effect (buff or debuff) to selected resources.
/// </summary>
public class ApplyBuffOp(ISelector sel, string effectType, IAmountResolver value, string duration, string? sourceId = null) : IEffectOp
{
    /// <summary>The selector used to pick target resources.</summary>
    public ISelector Selector => sel;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        long amount = value.Resolve(ctx);
        var targets = sel.Select(ctx);
        string source = sourceId ?? ctx.Source?.InstanceID ?? "";

        foreach (var target in targets)
        {
            target.TemporaryEffects.Add(new TemporaryEffect
            {
                EffectType = effectType,
                Value = amount,
                Duration = duration,
                SourceID = source,
            });
        }
    }
}
