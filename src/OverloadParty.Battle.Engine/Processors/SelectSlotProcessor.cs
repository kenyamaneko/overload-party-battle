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
        var cardDef = cc.MustGet(SlotSelectQueue.FindCard(state, pending).CardID);

        // 配置候補は選択を要求した時点で凍結せず、解決するこの時点の盤面から評価する。
        // 要求から解決までの間に破壊や別のデプロイで盤面が変わるため。
        string slotKey = $"{req.Zone}_{req.Index}";
        var field = state.GetField(playerNum);
        if (!ResourceHelpers.BuildValidZones(field, cardDef).Contains(slotKey))
        {
            throw new GameRuleException($"Invalid slot: {slotKey}");
        }

        // 配置先が決まったこの時点で初めてカードを領域から取り出す。
        var sourceCard = SlotSelectQueue.TakeCard(state, pending);
        var resource = SlotSelectQueue.BuildResource(
            pending, cardDef, sourceCard, state.NextInstanceID(), state.CurrentTurn);
        resource.DeployOrder = state.NextDeployOrder();

        Place(field, resource, req.Zone, req.Index);
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
                    CardId = resource.CardID,
                    InstanceId = resource.InstanceID,
                    Zone = req.Zone,
                    Index = req.Index,
                },
            },
        };

        // 配置時効果が残デプロイターンを 0 まで縮めたときの稼働開始処理は短縮した効果の中で済むため、
        // 稼働開始処理が二重に走らないよう、配置の時点で稼働前だったかを控える。
        bool wasDeploying = resource.DeployingTurnsLeft > 0;

        var (onSetEvents, onSetChoice) = OnSetFiring.Fire(
            state, game, playerNum, resource.CardID, resource.InstanceID,
            resource, supSource: null, choiceData: null, cc, effects);
        events.AddRange(onSetEvents);

        if (wasDeploying)
        {
            PassiveRecalculator.Recalculate(state, game, cc, effects);
        }
        else
        {
            OnSetFiring.RejectDeferredChoiceBeforeDeployCompletion(onSetChoice, resource.CardID);

            var (_, completionEvents) = DeployCompletion.CompleteResource(
                state, game, playerNum, resource, cc, effects);
            events.AddRange(completionEvents);
        }

        return new ActionResult
        {
            Events = events,
            ShouldSelectSlot = state.PendingSlotSelects.Count > 0,
        };
    }

    private static void Place(Field field, DeployedResource resource, string zone, int index)
    {
        var target = zone switch
        {
            Zones.Frontend => field.Frontend,
            Zones.Backend => field.Backend,
            _ => throw new GameRuleException($"Unknown zone: {zone}"),
        };

        target[index] = resource;
    }
}
