using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// 手札のカードをスロット選択待ちへ積む共通処理と、効果が配置スロットを要するかの判定。
/// </summary>
public static class SlotRequestHelpers
{
    /// <summary>
    /// 選んだ手札のカードを <see cref="AwaitingSlotSelect"/> として積みます。
    /// </summary>
    /// <param name="ctx">パイプライン実行コンテキスト。</param>
    /// <param name="choiceCardId">手札からデプロイするカードの ID。</param>
    public static void DeployFromHand(OpContext ctx, string choiceCardId)
    {
        var card = ctx.CardCache.MustGet(choiceCardId);

        var reserved = SlotSelectQueue.ReservedCardInstanceIDs(ctx.State, ctx.PlayerNum);
        var handCard = ctx.State.GetHand(ctx.PlayerNum)
            .FirstOrDefault(c => c.CardID == choiceCardId && !reserved.Contains(c.InstanceID))
            ?? throw new GameRuleException($"Card {choiceCardId} not in hand");

        // 配置先がなければ発動条件の不成立として不発にする。
        var field = ctx.GetField(ctx.PlayerNum);
        if (ResourceHelpers.BuildValidZones(field, card).Count == 0)
        {
            ctx.Result.HasGuardFailed = true;
            return;
        }

        // カードはスロットが決まるまで手札に残す。取り出さなければ不発時に戻す必要がなく、
        // 手札の並びもインスタンス ID も動かない。
        ctx.State.PendingSlotSelects.Add(new AwaitingSlotSelect
        {
            PlayerNum = ctx.PlayerNum,
            SourceZone = SlotSelectSources.Hand,
            CardInstanceID = handCard.InstanceID,
        });
    }

    /// <summary>
    /// 効果の op 列が、リソースを置くスロットを要求するかを返します。
    /// </summary>
    /// <param name="ops">効果の op 列。op 列を持たない効果では null。</param>
    /// <returns>配置スロットを要求する op を含めば true。</returns>
    public static bool RequiresPlacementSlot(IEffectOp[]? ops)
    {
        return ops is not null && ops.Any(op => op
            is RequestSlotFromRepoOp
            or RequestSlotFromHandOp
            or RequestSlotFromRepoSameCardOp
            or CustomFnTaggedOp { RequiresPlacementSlot: true });
    }
}
