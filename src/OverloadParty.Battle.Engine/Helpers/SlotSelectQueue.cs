using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// 効果デプロイのスロット選択待ちキューを、解決する時点の盤面を基準に扱う。
/// </summary>
public static class SlotSelectQueue
{
    /// <summary>
    /// 選択待ちのリソースを今の盤面で配置できるスロット一覧を返す。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="pending">対象のスロット選択待ち。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <returns>配置可能なスロットのワイヤー文字列一覧。</returns>
    public static List<string> ValidZonesFor(BattleGameState state, AwaitingSlotSelect pending, ICardCache cc)
    {
        var field = state.GetField(pending.PlayerNum);
        return ResourceHelpers.BuildValidZones(field, cc.MustGet(pending.Resource.CardID));
    }

    /// <summary>
    /// 配置できるスロットが残っていない選択待ちを不発として取り消し、取り出したカードを元の領域へ戻す。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    public static void CancelUnplaceable(BattleGameState state, ICardCache cc)
    {
        for (int i = state.PendingSlotSelects.Count - 1; i >= 0; i--)
        {
            var pending = state.PendingSlotSelects[i];
            if (ValidZonesFor(state, pending, cc).Count > 0) { continue; }

            ReturnToSource(state, pending);
            state.PendingSlotSelects.RemoveAt(i);
        }
    }

    private static void ReturnToSource(BattleGameState state, AwaitingSlotSelect pending)
    {
        var card = new UndeployedCard
        {
            InstanceID = pending.Resource.InstanceID,
            CardID = pending.Resource.CardID,
            ArtNo = pending.Resource.ArtNo,
        };

        switch (pending.SourceZone)
        {
            case SlotSelectSources.Hand:
                state.GetHand(pending.PlayerNum).Add(card);
                break;

            case SlotSelectSources.Repository:
                state.GetRepository(pending.PlayerNum).Add(card);
                break;

            default:
                throw new GameRuleException($"Unknown slot select source zone: {pending.SourceZone}");
        }
    }
}
