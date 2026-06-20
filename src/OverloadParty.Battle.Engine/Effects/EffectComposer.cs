using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// BuiltBlock または ops 列を EffectHandler に組み立てる。
/// guard 述語不成立時の扱いは trigger に応じて切り替え (Ignition = throw / その他 = 空の結果)。
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
                // Ignition は AvailableActions が事前 gate しているはずで、ここに来るのは
                // 不正リクエスト (A-1) なので例外で表面化する。それ以外のトリガー
                // (passive / 従属サブブロック) は guard 不成立が正常な不発のため HasGuardFailed=true。
                if (ctx.Trigger == TriggerType.Ignition)
                {
                    throw new GameRuleException("Ignition effect guard failed");
                }
                octx.Result.HasGuardFailed = true;
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
