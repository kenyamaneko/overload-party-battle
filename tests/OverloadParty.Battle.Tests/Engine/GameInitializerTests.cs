using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// Tests for GameInitializer based on RULEBOOK.md §2:
/// - 30-card deck shuffled → repository
/// - 5 initial hand cards
/// - Initial budget = 5000
/// - Initial insight pool = 0
/// - Game starts at T1, Draw phase
/// </summary>
public class GameInitializerTests
{
    private TestCardCache SetupCardCache()
    {
        var cc = new TestCardCache();
        // Add a compute card that covers all 30 deck slots
        cc.Add(TestFactory.ComputeCard(cardNo: 1, name: "Card1"));
        cc.Add(TestFactory.ComputeCard(cardNo: 2, name: "Card2"));
        cc.Add(TestFactory.ComputeCard(cardNo: 3, name: "Card3"));
        return cc;
    }

    [Fact]
    public void CreateNewGame_InitialBudget_Is5000()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck(1, 2, 3);

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        Assert.Equal(GameConstants.InitialBudget, state.Player1Budget);
        Assert.Equal(GameConstants.InitialBudget, state.Player2Budget);
        Assert.Equal(5000, state.Player1Budget);
    }

    [Fact]
    public void CreateNewGame_InitialInsightPool_IsZero()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck(1, 2, 3);

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        Assert.Equal(0, state.Player1InsightPool);
        Assert.Equal(0, state.Player2InsightPool);
    }

    /// <summary>
    /// Rulebook §2: 初期手札 5枚
    /// </summary>
    [Fact]
    public void CreateNewGame_Deals5Cards_PerPlayer()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck(1, 2, 3);

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        Assert.Equal(GameConstants.InitialHandSize, state.Player1Hand.Count);
        Assert.Equal(5, state.Player1Hand.Count);
        Assert.Equal(5, state.Player2Hand.Count);
    }

    /// <summary>
    /// Rulebook §2: 30枚 - 5枚手札 = 25枚リポジトリ
    /// </summary>
    [Fact]
    public void CreateNewGame_RemainingCards_GoToRepository()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck(1, 2, 3);

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        Assert.Equal(25, state.Player1Repository.Count);
        Assert.Equal(25, state.Player2Repository.Count);
    }

    [Fact]
    public void CreateNewGame_TotalCards_Equals30()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck(1, 2, 3);

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        int total1 = state.Player1Hand.Count + state.Player1Repository.Count;
        int total2 = state.Player2Hand.Count + state.Player2Repository.Count;

        Assert.Equal(GameConstants.DeckSize, total1);
        Assert.Equal(GameConstants.DeckSize, total2);
    }

    [Fact]
    public void CreateNewGame_StartsAtTurn1_DrawPhase()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck(1, 2, 3);

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        Assert.Equal(1, state.CurrentTurn);
        Assert.Equal(Phase.Draw, state.CurrentPhase);
    }

    [Fact]
    public void CreateNewGame_ActivePlayer_MatchesFirstPlayer()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck(1, 2, 3);

        var (_, state1) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);
        Assert.Equal(1, state1.ActivePlayer);

        var (_, state2) = GameInitializer.CreateNewGame("g2", "p1", "p2", deck, deck, 2, cc);
        Assert.Equal(2, state2.ActivePlayer);
    }

    [Fact]
    public void CreateNewGame_GameStatus_IsPlaying()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck(1, 2, 3);

        var (game, _) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        Assert.Equal(GameStatus.Playing, game.Status);
    }

    [Fact]
    public void CreateNewGame_PlayerIDs_AreSet()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck(1, 2, 3);

        var (game, _) = GameInitializer.CreateNewGame("g1", "alice", "bob", deck, deck, 1, cc);

        Assert.Equal("alice", game.Player1ID);
        Assert.Equal("bob", game.Player2ID);
    }

    /// <summary>
    /// Deck shuffling produces a random order — hand+repo should contain
    /// the same card IDs as the input deck (just reordered).
    /// </summary>
    [Fact]
    public void CreateNewGame_ShufflePreservesAllCards()
    {
        var cc = SetupCardCache();
        var deckCards = new List<long>();
        for (int i = 0; i < 10; i++) { deckCards.Add(1); deckCards.Add(2); deckCards.Add(3); }
        var deck = new DeckSnapshot { DeckID = "d1", Cards = deckCards };

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        var allP1CardIds = state.Player1Hand.Select(h => h.CardID)
            .Concat(state.Player1Repository.Select(r => r.CardID))
            .OrderBy(x => x)
            .ToList();

        var expectedSorted = deckCards.OrderBy(x => x).ToList();
        Assert.Equal(expectedSorted, allP1CardIds);
    }

    [Fact]
    public void CreateNewGame_InstanceIDs_AreUnique()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck(1, 2, 3);

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        var allIds = state.Player1Hand.Select(h => h.InstanceID)
            .Concat(state.Player1Repository.Select(r => r.InstanceID))
            .Concat(state.Player2Hand.Select(h => h.InstanceID))
            .Concat(state.Player2Repository.Select(r => r.InstanceID))
            .ToList();

        Assert.Equal(allIds.Distinct().Count(), allIds.Count);
    }

    [Fact]
    public void CreateNewGame_TimeBank_IsInitialized()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck(1, 2, 3);

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        Assert.Equal(GameConstants.InitialTimeBank, state.Player1TimeBank);
        Assert.Equal(GameConstants.InitialTimeBank, state.Player2TimeBank);
        Assert.Equal(480, state.Player1TimeBank);
    }

    [Fact]
    public void CreateNewGame_EmptyField_NoDeployedResources()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck(1, 2, 3);

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        Assert.All(state.Player1Field.Frontend, slot => Assert.Null(slot));
        Assert.All(state.Player1Field.Backend, slot => Assert.Null(slot));
        Assert.All(state.Player1Field.Support, slot => Assert.Null(slot));
    }
}
