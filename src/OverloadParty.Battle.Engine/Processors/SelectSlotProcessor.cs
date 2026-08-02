using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// SelectSlotProcessor は保留中の効果デプロイをプレイヤーが選択したスロットに配置して解決します
/// </summary>
public static class SelectSlotProcessor
{
    /// <summary>
    /// 保留中の効果デプロイをプレイヤーが選択したスロットに配置して解決します。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">対象ゲームのメタデータ。</param>
    /// <param name="playerNum">スロットを選択するプレイヤー番号。</param>
    /// <param name="req">選択されたスロットを含むリクエスト。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <returns>配置イベントと、後続スロット選択の要否を含むアクション結果。</returns>
    public static ActionResult Process(
        BattleGameState state, Game game, long playerNum,
        SelectSlotRequest req, ICardCache cc, IEffectRegistry effects)
    {
        if (state.PendingSlotSelects.Count == 0)
        {
            throw new GameRuleException("No pending slot selection");
        }

        // 双方に選択が溜まりうるので、キュー先頭ではなく送信者自身の最も古い選択を解決する。
        // 先頭だけを見ると、相手の選択が詰まっている間は自分の選択も解決できなくなる。
        int pendingIndex = state.PendingSlotSelects.FindIndex(p => p.PlayerNum == playerNum);
        if (pendingIndex < 0)
        {
            throw new GameRuleException("Slot selection is for a different player");
        }

        var pending = state.PendingSlotSelects[pendingIndex];

        string slotKey = $"{req.Zone}_{req.Index}";
        if (!pending.ValidZones.Contains(slotKey))
        {
            throw new GameRuleException($"Invalid slot: {slotKey}");
        }

        var field = state.GetField(playerNum);
        ValidateAndPlace(field, pending.Resource, req.Zone, req.Index);
        PassiveRecalculator.Recalculate(state, game, cc, effects);

        state.PendingSlotSelects.RemoveAt(pendingIndex);

        var events = new List<GameEvent>
        {
            new()
            {
                GameID = game.GameID,
                EventType = ActionTypes.SelectSlot,
                PlayerNum = playerNum,
                EventData = new SelectSlotEventData
                {
                    CardId = pending.Resource.CardID,
                    InstanceId = pending.Resource.InstanceID,
                    Zone = req.Zone,
                    Index = req.Index,
                },
            },
        };

        return new ActionResult
        {
            Events = events,
            ShouldSelectSlot = state.PendingSlotSelects.Count > 0,
        };
    }

    private static void ValidateAndPlace(Field field, DeployedResource resource, string zone, int index)
    {
        if (index < 0 || index >= BattleConstants.SlotsPerZone)
        {
            throw new GameRuleException($"Invalid slot index {index}");
        }

        switch (zone)
        {
            case Zones.Frontend:
                if (field.Frontend[index] is not null)
                {
                    throw new GameRuleException($"Frontend slot {index} is occupied");
                }
                field.Frontend[index] = resource;
                break;

            case Zones.Backend:
                if (field.Backend[index] is not null)
                {
                    throw new GameRuleException($"Backend slot {index} is occupied");
                }
                field.Backend[index] = resource;
                break;

            default:
                throw new GameRuleException($"Unknown zone: {zone}");
        }
    }
}
