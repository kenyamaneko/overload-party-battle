using OverloadParty.GameLogicConstants;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// BuiltBlock または ops 列を EffectHandler に組み立てる。
/// guard 述語が不成立のときの扱いは trigger に応じて切り替え (active = throw / passive = 空の結果)。
/// ops 内の GameRuleException は握り潰さず caller に素通りさせる。
/// </summary>
public static class EffectComposer
{
    /// <summary>
    /// guard 述語と ops を含む BuiltBlock から EffectHandler を組み立てます。
    /// </summary>
    /// <param name="block">組み立て対象のブロック。</param>
    /// <returns>handler。</returns>
    public static EffectHandler Compose(BuiltBlock block)
    {
        return ctx => RunBlock(block, ctx);
    }

    /// <summary>
    /// ops のみ (guard 述語なし) から EffectHandler を組み立てます。テスト向けの薄いラッパ。
    /// </summary>
    /// <param name="ops">構成する ops 列。</param>
    /// <returns>handler。</returns>
    public static EffectHandler Compose(params IEffectOp[] ops)
    {
        var block = new BuiltBlock { Ops = ops.ToArray() };
        return ctx => RunBlock(block, ctx);
    }

    /// <summary>
    /// ops のみ (guard 述語なし) を List 受けで組み立てます。
    /// </summary>
    /// <param name="ops">構成する ops 列。</param>
    /// <returns>handler。</returns>
    public static EffectHandler Compose(List<IEffectOp> ops)
    {
        var block = new BuiltBlock { Ops = ops.ToArray() };
        return ctx => RunBlock(block, ctx);
    }

    private static EffectResult RunBlock(BuiltBlock block, EffectContext ctx)
    {
        var octx = new OpContext(ctx);

        foreach (var guard in block.Guards)
        {
            if (!guard.Check(ctx))
            {
                // active 効果は AvailableActions が事前 gate しているはずなので、
                // 実行時の guard 不成立は不正リクエスト (A-1) として例外で表面化する。
                // passive 効果は guard 不成立が正常な不発なので GuardFailed=true を返す。
                if (ctx.Trigger is { } trigger && TriggerActivation.IsActive(trigger))
                {
                    throw new GameRuleException("Active effect guard failed");
                }
                octx.Result.GuardFailed = true;
                return octx.Result;
            }
        }

        foreach (var op in block.Ops)
        {
            op.Execute(octx);
            // choice op が ChoiceData 不足を検知して PendingChoice を立てたら、以降の op は実行しない。
            if (octx.Result.PendingChoice is not null) { break; }
        }
        return octx.Result;
    }
}
