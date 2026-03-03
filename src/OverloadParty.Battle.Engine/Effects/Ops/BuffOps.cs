using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

public class ApplyBuffOp(ISelector sel, string effectType, IAmountResolver value, string duration, string? sourceId = null) : IEffectOp
{
    public ISelector Selector => sel;

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
