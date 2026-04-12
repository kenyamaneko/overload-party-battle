using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// ApplyBuffOp は選択されたリソースに一時効果（バフ/デバフ）を適用します
/// </summary>
public class ApplyBuffOp(ISelector sel, string effectType, IAmountResolver value, string duration, string? sourceId = null, string mode = "") : IEffectOp
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
                Mode = mode,
            });
        }
    }
}
