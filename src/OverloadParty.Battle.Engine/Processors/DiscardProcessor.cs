using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// Processes discard actions during the end phase when a player's hand exceeds the limit.
/// </summary>
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
    /// <returns>The action result containing discard events and possible game-over result.</returns>
    public static ActionResult Process(
        GameState state, Game game, long playerNum,
        DiscardHandRequest req, ICardCache cc, IEffectRegistry? effects = null)
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
            }.ToDictionary(),
        });

        var result = new ActionResult { Events = events, StateUpdated = true };

        if (WinConditionChecker.CheckLaunchFailure(state, playerNum))
        {
            result.GameOver = new GameOverResult(
                state.OpponentOf(playerNum),
                WinReason.LaunchFailure.ToWireString());
            return result;
        }

        TurnManager.SwitchActivePlayer(state);
        var gameOverResult = DrawPhaseProcessor.Process(state, game, cc, effects);

        if (gameOverResult is not null)
        {
            result.GameOver = gameOverResult;
        }

        return result;
    }
}
