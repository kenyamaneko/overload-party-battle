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
    /// <param name="initiatives">ID から施策を解決するカタログ。</param>
    /// <returns>施策イベントを含むアクション結果。</returns>
    public static ActionResult Process(
        BattleGameState state, Game game, long playerNum,
        UseInitiativeRequest req, ICardCache cc, IEffectRegistry effects, IInitiativeCatalog initiatives)
    {
        if (TurnManager.IsFirstTurn(state.CurrentTurn))
        {
            throw new GameRuleException("cannot use initiative on first turn");
        }

        CheckUsageLimit(state, playerNum, req.Kind);

        string initiativeId = InitiativeSelection.ResolveId(state, playerNum, req.Kind);
        var initiative = initiatives.GetById(initiativeId)
            ?? throw new GameRuleException($"initiative '{initiativeId}' not found");
        if (initiative.Kind != req.Kind)
        {
            throw new GameRuleException($"initiative '{initiativeId}' is not a {req.Kind}");
        }

        long pool = state.GetInsightPool(playerNum);
        if (pool < initiative.InsightCost)
        {
            throw new GameRuleException(
                $"insufficient insight: have {pool}, need {initiative.InsightCost}");
        }

        var handler = effects.Get(initiative.EffectSourceId, TriggerType.Ignition)
            ?? throw new GameRuleException($"no handler for initiative '{initiative.InitiativeId}'");

        // 対象を選ぶ施策は、選択値が無いと対象 0 件のまま静かに完了する。
        // コストと使用回数を消費してから空振りするので、実行前に拒否する。
        var effectInfo = effects.GetEffectInfo(initiative.EffectSourceId, TriggerType.Ignition);
        if (effectInfo?.TargetType == EffectTargetType.Choice && req.ChoiceData is null)
        {
            throw new GameRuleException(
                $"initiative '{initiative.InitiativeId}' requires a target choice");
        }

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
            // 施策は盤面に実体を持たないので、選択待ちからの再開に使う同定情報を施策 ID で与える。
            EffectCardId = initiative.EffectSourceId,
            EffectInstanceId = initiative.InitiativeId,
        };
        var result = handler(ctx);

        // 選択待ちを捨てると、選択を要する施策がコストだけ払って何も起きずに終わる。
        state.PendingEffectChoice = result.PendingChoice ?? state.PendingEffectChoice;

        MarkUsed(state, playerNum, initiative.Kind);

        var events = new List<GameEvent>(result.Events);
        events.Insert(0, new GameEvent
        {
            GameID = game.GameID,
            EventType = EventTypes.UseInitiative,
            PlayerNum = playerNum,
            EventData = new UseInitiativeEventData
            {
                ProductId = initiative.ProductId,
                InitiativeId = initiative.InitiativeId,
                Kind = initiative.Kind,
                InitiativeName = initiative.Name,
                InsightCost = initiative.InsightCost,
            },
        });

        return new ActionResult { Events = events };
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
