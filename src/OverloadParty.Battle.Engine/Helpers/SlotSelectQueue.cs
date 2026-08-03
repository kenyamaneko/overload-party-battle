using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// 効果デプロイのスロット選択待ちキューを、解決する時点の盤面を基準に扱う。
/// </summary>
public static class SlotSelectQueue
{
    /// <summary>
    /// 選択待ちが指すカードを、置かれている領域から取り出さずに返す。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="pending">対象のスロット選択待ち。</param>
    /// <returns>配置を待っているカード。</returns>
    public static UndeployedCard FindCard(BattleGameState state, AwaitingSlotSelect pending)
    {
        return SourceZoneOf(state, pending).FirstOrDefault(c => c.InstanceID == pending.CardInstanceID)
            ?? throw new GameRuleException(
                $"card {pending.CardInstanceID} awaiting slot select is no longer in {pending.SourceZone}");
    }

    /// <summary>
    /// 選択待ちが指すカードを、置かれている領域から取り除いて返す。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="pending">対象のスロット選択待ち。</param>
    /// <returns>取り出したカード。</returns>
    public static UndeployedCard TakeCard(BattleGameState state, AwaitingSlotSelect pending)
    {
        var card = FindCard(state, pending);
        SourceZoneOf(state, pending).Remove(card);
        return card;
    }

    /// <summary>
    /// 選択待ちのカードを今の盤面で配置できるスロット一覧を返す。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="pending">対象のスロット選択待ち。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <returns>配置可能なスロットのワイヤー文字列一覧。</returns>
    public static List<string> ValidZonesFor(BattleGameState state, AwaitingSlotSelect pending, ICardCache cc)
    {
        var field = state.GetField(pending.PlayerNum);
        return ResourceHelpers.BuildValidZones(field, cc.MustGet(FindCard(state, pending).CardID));
    }

    /// <summary>
    /// 選択待ちのカードから、フィールドへ置くリソースを組み立てる。
    /// </summary>
    /// <param name="pending">対象のスロット選択待ち。</param>
    /// <param name="card">配置するカードの定義。</param>
    /// <param name="sourceCard">配置を待っているカード。</param>
    /// <param name="instanceID">生成するリソースへ付与するインスタンス ID。</param>
    /// <param name="deployTurn">デプロイされたターン番号。</param>
    /// <returns>生成したリソースインスタンス。</returns>
    public static DeployedResource BuildResource(
        AwaitingSlotSelect pending, CardDefinition card, UndeployedCard sourceCard,
        string instanceID, long deployTurn)
    {
        var resource = ResourceHelpers.CreateDeployedResource(card, instanceID, deployTurn, sourceCard.ArtNo);

        if (pending.OverrideAV > 0)
        {
            resource.MaxAV = pending.OverrideAV;
            resource.Damage = 0;
        }

        return resource;
    }

    /// <summary>
    /// 選択待ちが既に配置予約しているカードのインスタンス ID を返す。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="playerNum">対象プレイヤー番号 (1 または 2)。</param>
    /// <returns>予約済みカードのインスタンス ID 集合。</returns>
    public static HashSet<string> ReservedCardInstanceIDs(BattleGameState state, long playerNum)
    {
        return state.PendingSlotSelects
            .Where(p => p.PlayerNum == playerNum)
            .Select(p => p.CardInstanceID)
            .ToHashSet();
    }

    /// <summary>
    /// 配置できるスロットが残っていない選択待ちを、発動条件の不成立として取り消す。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    public static void CancelUnplaceable(BattleGameState state, ICardCache cc)
    {
        for (int i = state.PendingSlotSelects.Count - 1; i >= 0; i--)
        {
            if (ValidZonesFor(state, state.PendingSlotSelects[i], cc).Count > 0) { continue; }

            state.PendingSlotSelects.RemoveAt(i);
        }
    }

    private static List<UndeployedCard> SourceZoneOf(BattleGameState state, AwaitingSlotSelect pending) =>
        pending.SourceZone switch
        {
            SlotSelectSources.Hand => state.GetHand(pending.PlayerNum),
            SlotSelectSources.Repository => state.GetRepository(pending.PlayerNum),
            _ => throw new GameRuleException($"Unknown slot select source zone: {pending.SourceZone}"),
        };
}
