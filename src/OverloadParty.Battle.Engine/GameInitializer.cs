using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// GameInitializer はゲームの新規作成と初期化を担当します
/// </summary>
public static class GameInitializer
{
    /// <summary>
    /// Create a new game with shuffled decks and initial hands.
    /// Returns (game, gameState).
    /// </summary>
    /// <param name="gameID">ゲームの ID。</param>
    /// <param name="deck1">プレイヤー 1 のデッキスナップショット。</param>
    /// <param name="deck2">プレイヤー 2 のデッキスナップショット。</param>
    /// <param name="firstPlayer">先攻プレイヤー番号 (1 または 2)。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <returns>初期化された Game とゲーム状態。</returns>
    public static (Game Game, BattleGameState State) CreateNewGame(
        string gameID,
        DeckSnapshot deck1,
        DeckSnapshot deck2,
        long firstPlayer,
        ICardCache cc)
    {
        ValidateDeck(1, deck1, cc);
        ValidateDeck(2, deck2, cc);

        var game = new Game
        {
            GameID = gameID,
            Status = GameStatus.Playing,
            FirstPlayer = (int)firstPlayer,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var state = new BattleGameState
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

        // 両プレイヤーにシャッフルして配る
        var rng = Random.Shared;

        DealCards(state, 1, deck1.Cards, rng);
        DealCards(state, 2, deck2.Cards, rng);

        return (game, state);
    }

    private static void ValidateDeck(long playerNum, DeckSnapshot deck, ICardCache cc)
    {
        var missing = deck.Cards
            .Select(c => c.CardId)
            .Where(id => cc.Get(id) is null)
            .Distinct()
            .ToList();

        if (missing.Count > 0)
        {
            throw new GameRuleException(
                $"deck for player {playerNum} references unknown card_id(s): {string.Join(", ", missing)}");
        }
    }

    private static void DealCards(BattleGameState state, long playerNum, List<DeckSnapshotCard> cards, Random rng)
    {
        // シャッフル
        var shuffled = new List<DeckSnapshotCard>(cards);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        // 初期手札を配る
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

        // 残りのカードをリポジトリに入れる
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
