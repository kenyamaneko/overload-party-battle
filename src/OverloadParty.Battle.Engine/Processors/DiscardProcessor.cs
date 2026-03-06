using System.Linq;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Processors;

public static class DiscardProcessor
{
    public static ActionResult Process(
        GameState state, Game game, long playerNum,
        DiscardHandRequest req, ICardCache cc)
    {
        var hand = state.GetHand(playerNum);
        int requiredDiscards = hand.Count - GameConstants.HandLimit;

        if (requiredDiscards <= 0)
            throw new GameRuleException("no discard needed");

        if (req.CardInstanceIDs.Count != requiredDiscards)
            throw new GameRuleException($"must discard exactly {requiredDiscards} cards, got {req.CardInstanceIDs.Count}");

        // Validate all cards exist in hand and remove them
        var discardSet = new HashSet<string>(req.CardInstanceIDs);
        var discardedCards = new List<HandCard>();

        for (int i = hand.Count - 1; i >= 0; i--)
        {
            if (discardSet.Remove(hand[i].InstanceID))
            {
                discardedCards.Add(hand[i]);
                hand.RemoveAt(i);
            }
        }

        if (discardSet.Any())
            throw new GameRuleException($"some cards not found in hand: {string.Join(", ", discardSet)}");

        // Move discarded cards to trash
        foreach (var card in discardedCards)
        {
            FieldHelpers.AddToTrash(state, playerNum, card.CardID, card.InstanceID);
        }

        var events = new List<GameEvent>();
        var playerId = playerNum == 1 ? game.Player1ID : game.Player2ID;

        events.Add(new GameEvent
        {
            GameID = game.GameID,
            EventType = WireActionTypes.DiscardHand,
            PlayerID = playerId,
            EventData = new Dictionary<string, object>
            {
                ["discardedCount"] = requiredDiscards,
                ["discardedIds"] = req.CardInstanceIDs,
            }
        });

        var result = new ActionResult { Events = events, StateUpdated = true };

        // Check launch failure
        if (WinConditionChecker.CheckLaunchFailure(state, playerNum))
        {
            var winnerNum = state.OpponentOf(playerNum);
            result.GameOver = true;
            result.WinnerNum = winnerNum;
            result.WinReason = WinReason.LaunchFailure.ToWireString();
            return result;
        }

        // Switch player and auto-advance
        TurnManager.SwitchActivePlayer(state);
        var (gameOver, winReason) = TurnManager.AutoAdvancePhases(state, game, cc);

        if (gameOver)
        {
            result.GameOver = true;
            result.WinReason = winReason;

            // Determine winner from win condition check
            var (winnerNum2, _, _) = WinConditionChecker.Check(state, game);
            result.WinnerNum = winnerNum2;
        }

        return result;
    }
}
