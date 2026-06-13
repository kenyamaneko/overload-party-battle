using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Ports;
using OverloadParty.Battle.Models;
using OverloadParty.GameLogicConstants;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// UseInitiativeProcessor はプレイヤーのプロダクトの施策 (ルーチン / スペシャル) を処理します。
/// </summary>
public static class UseInitiativeProcessor
{
    /// <summary>
    /// プレイヤーの陣営からプロダクトを解決し、指定された施策を発動します。
    /// 使用回数 (ルーチン 1ターン1回 / スペシャル 1ゲーム1回)・先攻 T1 制限・Insight コストを
    /// 検証し、効果を起動効果と同じパイプラインで実行します。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">ゲームメタデータ。</param>
    /// <param name="playerNum">施策を使用するプレイヤー番号。</param>
    /// <param name="req">施策の区分と選択データを含むリクエスト。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <param name="products">陣営からプロダクトを解決するカタログ。</param>
    /// <returns>施策イベントと状態更新フラグを含むアクション結果。</returns>
    public static ActionResult Process(
        BattleGameState state, Game game, long playerNum,
        UseInitiativeRequest req, ICardCache cc, IEffectRegistry effects, IProductCatalog products)
    {
        if (TurnManager.IsFirstTurn(state.CurrentTurn))
        {
            throw new GameRuleException("cannot use initiative on first turn");
        }

        string faction = state.GetFaction(playerNum);
        var product = products.GetByFaction(faction)
            ?? throw new GameRuleException($"no product for faction '{faction}'");

        var initiative = product.Initiatives.FirstOrDefault(i => i.Kind == req.Kind)
            ?? throw new GameRuleException($"unknown initiative kind '{req.Kind}'");

        CheckUsageLimit(state, playerNum, initiative.Kind);

        long pool = state.GetInsightPool(playerNum);
        if (pool < initiative.InsightCost)
        {
            throw new GameRuleException(
                $"insufficient insight: have {pool}, need {initiative.InsightCost}");
        }

        var handler = effects.Get(InitiativeEffects.HandlerCardId(faction, initiative.Kind), TriggerType.Ignition)
            ?? throw new GameRuleException($"no handler for initiative {faction}/{initiative.Kind}");

        state.SetInsightPool(playerNum, pool - initiative.InsightCost);

        var ctx = new EffectContext
        {
            State = state,
            Game = game,
            PlayerNum = playerNum,
            CardCache = cc,
            ChoiceData = req.ChoiceData,
            Effects = effects,
            Trigger = TriggerType.Ignition,
        };
        var result = handler(ctx);

        MarkUsed(state, playerNum, initiative.Kind);

        var events = new List<GameEvent>(result.Events);
        events.Insert(0, new GameEvent
        {
            GameID = game.GameID,
            EventType = EventTypes.UseInitiative,
            PlayerNum = playerNum,
            EventData = new UseInitiativeEventData
            {
                Faction = faction,
                Kind = initiative.Kind,
                InitiativeName = initiative.Name,
                InsightCost = initiative.InsightCost,
            },
        });

        return new ActionResult { Events = events, StateUpdated = true };
    }

    private static void CheckUsageLimit(BattleGameState state, long playerNum, string kind)
    {
        switch (kind)
        {
            case InitiativeKinds.Routine:
                if (state.GetRoutineUsedThisTurn(playerNum))
                {
                    throw new GameRuleException("routine already used this turn");
                }
                break;
            case InitiativeKinds.Special:
                if (state.GetSpecialUsedThisGame(playerNum))
                {
                    throw new GameRuleException("special already used this game");
                }
                break;
            default:
                throw new GameRuleException($"unknown initiative kind '{kind}'");
        }
    }

    private static void MarkUsed(BattleGameState state, long playerNum, string kind)
    {
        switch (kind)
        {
            case InitiativeKinds.Routine:
                state.SetRoutineUsedThisTurn(playerNum, true);
                break;
            case InitiativeKinds.Special:
                state.SetSpecialUsedThisGame(playerNum, true);
                break;
            default:
                throw new GameRuleException($"unknown initiative kind '{kind}'");
        }
    }
}
