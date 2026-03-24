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
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", name: "Card1"));
        cc.Add(TestFactory.ComputeCard(cardId: "TEST-0002", name: "Card2"));
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0002", name: "Card3"));
        return cc;
    }

    [Fact]
    public void CreateNewGame_InitialBudget_Is5000()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("SH-0001", "SH-0002", "SH-0002");

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        state.Player1Budget.Should().Be(GameConstants.InitialBudget);
        state.Player2Budget.Should().Be(GameConstants.InitialBudget);
        state.Player1Budget.Should().Be(5000);
    }

    [Fact]
    public void CreateNewGame_InitialInsightPool_IsZero()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("SH-0001", "SH-0002", "SH-0002");

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        state.Player1InsightPool.Should().Be(0);
        state.Player2InsightPool.Should().Be(0);
    }

    /// <summary>
    /// 初期手札 5枚
    /// </summary>
    [Fact]
    public void CreateNewGame_Deals5Cards_PerPlayer()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("SH-0001", "SH-0002", "SH-0002");

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        state.Player1Hand.Should().HaveCount(GameConstants.InitialHandSize);
        state.Player1Hand.Should().HaveCount(5);
        state.Player2Hand.Should().HaveCount(5);
    }

    /// <summary>
    /// 30枚 - 5枚手札 = 25枚リポジトリ
    /// </summary>
    [Fact]
    public void CreateNewGame_RemainingCards_GoToRepository()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("SH-0001", "SH-0002", "SH-0002");

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        state.Player1Repository.Should().HaveCount(25);
        state.Player2Repository.Should().HaveCount(25);
    }

    [Fact]
    public void CreateNewGame_TotalCards_Equals30()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("SH-0001", "SH-0002", "SH-0002");

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        int total1 = state.Player1Hand.Count + state.Player1Repository.Count;
        int total2 = state.Player2Hand.Count + state.Player2Repository.Count;

        total1.Should().Be(GameConstants.DeckSize);
        total2.Should().Be(GameConstants.DeckSize);
    }

    [Fact]
    public void CreateNewGame_StartsAtTurn1_DrawPhase()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("SH-0001", "SH-0002", "SH-0002");

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        state.CurrentTurn.Should().Be(1);
        state.CurrentPhase.Should().Be(Phase.Draw);
    }

    [Fact]
    public void CreateNewGame_ActivePlayer_MatchesFirstPlayer()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("SH-0001", "SH-0002", "SH-0002");

        var (_, state1) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);
        state1.ActivePlayer.Should().Be(1);

        var (_, state2) = GameInitializer.CreateNewGame("g2", "p1", "p2", deck, deck, 2, cc);
        state2.ActivePlayer.Should().Be(2);
    }

    [Fact]
    public void CreateNewGame_GameStatus_IsPlaying()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("SH-0001", "SH-0002", "SH-0002");

        var (game, _) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        game.Status.Should().Be(GameStatus.Playing);
    }

    [Fact]
    public void CreateNewGame_PlayerIDs_AreSet()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("SH-0001", "SH-0002", "SH-0002");

        var (game, _) = GameInitializer.CreateNewGame("g1", "alice", "bob", deck, deck, 1, cc);

        game.Player1ID.Should().Be("alice");
        game.Player2ID.Should().Be("bob");
    }

    /// <summary>
    /// Deck shuffling produces a random order — hand+repo should contain
    /// the same card IDs as the input deck (just reordered).
    /// </summary>
    [Fact]
    public void CreateNewGame_ShufflePreservesAllCards()
    {
        var cc = SetupCardCache();
        var deckCards = new List<DeckSnapshotCard>();
        for (int i = 0; i < 10; i++)
        {
            deckCards.Add(new DeckSnapshotCard { CardId = "SH-0001" });
            deckCards.Add(new DeckSnapshotCard { CardId = "TEST-0002" });
            deckCards.Add(new DeckSnapshotCard { CardId = "SH-0002" });
        }
        var deck = new DeckSnapshot { DeckID = "d1", Cards = deckCards };

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        var allP1CardIds = state.Player1Hand.Select(h => h.CardID)
            .Concat(state.Player1Repository.Select(r => r.CardID))
            .OrderBy(x => x)
            .ToList();

        var expectedSorted = deckCards.Select(c => c.CardId).OrderBy(x => x).ToList();
        allP1CardIds.Should().Equal(expectedSorted);
    }

    [Fact]
    public void CreateNewGame_InstanceIDs_AreUnique()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("SH-0001", "SH-0002", "SH-0002");

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        var allIds = state.Player1Hand.Select(h => h.InstanceID)
            .Concat(state.Player1Repository.Select(r => r.InstanceID))
            .Concat(state.Player2Hand.Select(h => h.InstanceID))
            .Concat(state.Player2Repository.Select(r => r.InstanceID))
            .ToList();

        allIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void CreateNewGame_TimeBank_IsInitialized()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("SH-0001", "SH-0002", "SH-0002");

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        state.Player1TimeBank.Should().Be(GameConstants.InitialTimeBank);
        state.Player2TimeBank.Should().Be(GameConstants.InitialTimeBank);
        state.Player1TimeBank.Should().Be(480);
    }

    [Fact]
    public void CreateNewGame_EmptyField_NoDeployedResources()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("SH-0001", "SH-0002", "SH-0002");

        var (_, state) = GameInitializer.CreateNewGame("g1", "p1", "p2", deck, deck, 1, cc);

        state.Player1Field.Frontend.Should().AllSatisfy(slot => slot.Should().BeNull());
        state.Player1Field.Backend.Should().AllSatisfy(slot => slot.Should().BeNull());
        state.Player1Field.Support.Should().AllSatisfy(slot => slot.Should().BeNull());
    }
}
