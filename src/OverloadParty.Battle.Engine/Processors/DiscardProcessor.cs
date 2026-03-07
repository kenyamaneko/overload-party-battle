using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Processors;

public static class DiscardProcessor
{
    /// <summary>
    /// End フェーズ用: 手札上限を超えた分を捨てさせ、ターン交代 → ドローフェーズへ進む。
    /// </summary>
    public static ActionResult Process(
        GameState state, Game game, long playerNum,
        DiscardHandRequest req, ICardCache cc)
    {
        var hand = state.GetHand(playerNum);
        int requiredDiscards = hand.Count - GameConstants.HandLimit;

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
        var playerId = game.GetPlayerID(playerNum);

        events.Add(new GameEvent
        {
            GameID = game.GameID,
            EventType = WireActionTypes.DiscardHand,
            PlayerID = playerId,
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
        var gameOverResult = DrawPhaseProcessor.Process(state, game, cc);

        if (gameOverResult is not null)
        {
            result.GameOver = gameOverResult;
        }

        return result;
    }
}
