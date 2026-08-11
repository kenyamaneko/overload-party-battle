using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// DiscardProcessor はエンドフェーズで手札上限超過時のディスカードアクションを処理します
/// </summary>
/// <remarks>
/// ターン全体のタイムバンクを時間の上限として使うため、手札破棄には個別のタイムアウトを持たせない。
/// </remarks>
public static class DiscardProcessor
{
    /// <summary>
    /// End フェーズ用: 手札上限を超えた分を捨てさせ、ターン交代 → ドローフェーズへ進む。
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="game">The game metadata.</param>
    /// <param name="playerNum">The player number performing the discard.</param>
    /// <param name="req">The discard request containing card instance IDs to discard.</param>
    /// <param name="cc">The card definition cache.</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <param name="clock">現在時刻の供給元。</param>
    /// <returns>The action result containing discard events and possible game-over result.</returns>
    public static ActionResult Process(
        BattleGameState state, Game game, long playerNum,
        DiscardHandRequest req, ICardCache cc, IEffectRegistry effects, IClock clock)
    {
        var hand = state.GetHand(playerNum);
        int requiredDiscards = hand.Count - BattleConstants.HandLimit;

        if (requiredDiscards <= 0)
        {
            throw new GameRuleException("no discard needed");
        }

        if (req.CardInstanceIDs.Count != requiredDiscards)
        {
            throw new GameRuleException($"must discard exactly {requiredDiscards} cards, got {req.CardInstanceIDs.Count}");
        }

        var discardedCount = CardMoveHelpers.DiscardCards(state, playerNum, req.CardInstanceIDs);

        var events = new List<GameEvent>();

        events.Add(new GameEvent
        {
            GameID = game.GameID,
            EventType = ActionTypes.DiscardHand,
            PlayerNum = playerNum,
            EventData = new DiscardHandEventData
            {
                DiscardedCount = discardedCount,
                DiscardedIds = req.CardInstanceIDs,
            },
        });

        var advance = EndPhaseProcessor.AdvanceAfterHandAdjustment(
            state, game, playerNum, cc, effects, clock);

        return new ActionResult
        {
            Events = events,
            GameOver = advance.GameOver,
        };
    }
}
