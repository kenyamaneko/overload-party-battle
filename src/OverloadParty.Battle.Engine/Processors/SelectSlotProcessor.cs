using OverloadParty.Battle.Models;

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
    /// <returns>配置イベントと、後続スロット選択の要否を含むアクション結果。</returns>
    public static ActionResult Process(
        BattleGameState state, Game game, long playerNum,
        SelectSlotRequest req, ICardCache cc)
    {
        if (state.PendingSlotSelects.Count == 0)
        {
            throw new GameRuleException("No pending slot selection");
        }

        var pending = state.PendingSlotSelects[0];

        if (pending.PlayerNum != playerNum)
        {
            throw new GameRuleException("Slot selection is for a different player");
        }

        string slotKey = $"{req.Zone}_{req.Index}";
        if (!pending.ValidZones.Contains(slotKey))
        {
            throw new GameRuleException($"Invalid slot: {slotKey}");
        }

        var field = state.GetField(playerNum);
        ValidateAndPlace(field, pending.Resource, req.Zone, req.Index);

        state.PendingSlotSelects.RemoveAt(0);

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
