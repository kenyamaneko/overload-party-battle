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
        DeckSnapshot deck1,
        DeckSnapshot deck2,
        long firstPlayer,
        ICardCache cc)
    {
        var game = new Game
        {
            GameID = gameID,
            Status = GameStatus.Playing,
            FirstPlayer = (int)firstPlayer,
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
            Player1Budget = BattleConstants.InitialBudget,
            Player1InsightPool = BattleConstants.InitialInsightPool,
            Player1TimeBank = BattleConstants.InitialTimeBank,
            Player2Budget = BattleConstants.InitialBudget,
            Player2InsightPool = BattleConstants.InitialInsightPool,
            Player2TimeBank = BattleConstants.InitialTimeBank,
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
        var hand = new List<UndeployedCard>();
        int handSize = Math.Min(BattleConstants.InitialHandSize, shuffled.Count);
        for (int i = 0; i < handSize; i++)
        {
            hand.Add(new UndeployedCard
            {
                InstanceID = state.NextInstanceID(),
                CardID = shuffled[i].CardId,
                ArtNo = shuffled[i].ArtNo,
            });
        }

        // Remaining cards go to repository
        var repo = new List<UndeployedCard>();
        for (int i = handSize; i < shuffled.Count; i++)
        {
            repo.Add(new UndeployedCard
            {
                InstanceID = state.NextInstanceID(),
                CardID = shuffled[i].CardId,
                ArtNo = shuffled[i].ArtNo,
            });
        }

        state.SetHand(playerNum, hand);
        state.SetRepository(playerNum, repo);
    }
}
