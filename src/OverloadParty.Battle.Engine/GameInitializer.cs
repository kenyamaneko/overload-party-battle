using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Creates and initializes new games.
/// </summary>
public static class GameInitializer
{
    /// <summary>
    /// Create a new game with shuffled decks and initial hands.
    /// Returns (game, gameState).
    /// </summary>
    public static (Game Game, GameState State) CreateNewGame(
        string gameID,
        string player1ID,
        string player2ID,
        DeckSnapshot deck1,
        DeckSnapshot deck2,
        long firstPlayer,
        ICardCache cc)
    {
        var game = new Game
        {
            GameID = gameID,
            Player1ID = player1ID,
            Player2ID = player2ID,
            Player1DeckSnapshot = deck1,
            Player2DeckSnapshot = deck2,
            Status = GameStatus.Playing,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var state = new GameState
        {
            GameID = gameID,
            Version = 1,
            CurrentTurn = 1,
            CurrentPhase = Phase.Draw,
            ActivePlayer = firstPlayer,
            Player1Budget = GameConstants.InitialBudget,
            Player1InsightPool = GameConstants.InitialInsightPool,
            Player1TimeBank = GameConstants.InitialTimeBank,
            Player2Budget = GameConstants.InitialBudget,
            Player2InsightPool = GameConstants.InitialInsightPool,
            Player2TimeBank = GameConstants.InitialTimeBank,
            TurnStartedAt = DateTime.UtcNow,
            NextInstanceSeq = 1,
            UpdatedAt = DateTime.UtcNow,
        };

        // Shuffle and deal for both players
        var rng = Random.Shared;

        DealCards(state, 1, deck1.Cards, rng);
        DealCards(state, 2, deck2.Cards, rng);

        return (game, state);
    }

    private static void DealCards(GameState state, long playerNum, List<DeckSnapshotCard> cards, Random rng)
    {
        // Shuffle
        var shuffled = new List<DeckSnapshotCard>(cards);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        // Deal initial hand
        var hand = new List<HandCard>();
        int handSize = Math.Min(GameConstants.InitialHandSize, shuffled.Count);
        for (int i = 0; i < handSize; i++)
        {
            hand.Add(new HandCard
            {
                InstanceID = state.NextInstanceID(),
                CardID = shuffled[i].CardNo,
                ArtNo = shuffled[i].ArtNo,
            });
        }

        // Remaining cards go to repository
        var repo = new List<HandCard>();
        for (int i = handSize; i < shuffled.Count; i++)
        {
            repo.Add(new HandCard
            {
                InstanceID = state.NextInstanceID(),
                CardID = shuffled[i].CardNo,
                ArtNo = shuffled[i].ArtNo,
            });
        }

        state.SetHand(playerNum, hand);
        state.SetRepository(playerNum, repo);
    }
}
