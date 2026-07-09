using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

[Trait("対象", "ゲーム初期化")]
public class GameInitializerTests
{
    private TestCardCache SetupCardCache()
    {
        var cc = new TestCardCache();
        // Add a compute card that covers all 30 deck slots
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", name: "Card1"));
        cc.Add(TestFactory.ComputeCard(cardId: "TEST-0002", name: "Card2"));
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0002", name: "Card3"));
        return cc;
    }

    [Fact(DisplayName = "新規ゲームでは両プレイヤーのバジェットが 5000 になる")]
    public void CreateNewGame_InitialBudget_Is5000()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("TST-0001", "TST-0002", "TST-0002");

        var (_, state) = GameInitializer.CreateNewGame("g1", deck, deck, 1, cc);

        state.Player1Budget.Should().Be(BattleConstants.InitialBudget);
        state.Player2Budget.Should().Be(BattleConstants.InitialBudget);
        state.Player1Budget.Should().Be(5000);
    }

    [Fact(DisplayName = "新規ゲームでは両プレイヤーのインサイトプールが 0 になる")]
    public void CreateNewGame_InitialInsightPool_IsZero()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("TST-0001", "TST-0002", "TST-0002");

        var (_, state) = GameInitializer.CreateNewGame("g1", deck, deck, 1, cc);

        state.Player1InsightPool.Should().Be(0);
        state.Player2InsightPool.Should().Be(0);
    }

    [Fact(DisplayName = "新規ゲームでは各プレイヤーに手札が 5 枚配られる")]
    public void CreateNewGame_Deals5Cards_PerPlayer()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("TST-0001", "TST-0002", "TST-0002");

        var (_, state) = GameInitializer.CreateNewGame("g1", deck, deck, 1, cc);

        state.Player1Hand.Should().HaveCount(BattleConstants.InitialHandSize);
        state.Player1Hand.Should().HaveCount(5);
        state.Player2Hand.Should().HaveCount(5);
    }

    [Fact(DisplayName = "新規ゲームでは手札 5 枚を除いた 25 枚がデッキに残る")]
    public void CreateNewGame_RemainingCards_GoToRepository()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("TST-0001", "TST-0002", "TST-0002");

        var (_, state) = GameInitializer.CreateNewGame("g1", deck, deck, 1, cc);

        state.Player1Repository.Should().HaveCount(25);
        state.Player2Repository.Should().HaveCount(25);
    }

    [Fact(DisplayName = "新規ゲームでは手札とデッキの合計が 30 枚になる")]
    public void CreateNewGame_TotalCards_Equals30()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("TST-0001", "TST-0002", "TST-0002");

        var (_, state) = GameInitializer.CreateNewGame("g1", deck, deck, 1, cc);

        int total1 = state.Player1Hand.Count + state.Player1Repository.Count;
        int total2 = state.Player2Hand.Count + state.Player2Repository.Count;

        total1.Should().Be(InitialValues.DeckSize);
        total2.Should().Be(InitialValues.DeckSize);
    }

    [Fact(DisplayName = "新規ゲームはターン 1 のドローフェーズで始まる")]
    public void CreateNewGame_StartsAtTurn1_DrawPhase()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("TST-0001", "TST-0002", "TST-0002");

        var (_, state) = GameInitializer.CreateNewGame("g1", deck, deck, 1, cc);

        state.CurrentTurn.Should().Be(1);
        state.CurrentPhase.Should().Be(Phase.Draw);
    }

    [Fact(DisplayName = "アクティブプレイヤーが指定した先攻プレイヤーになる")]
    public void CreateNewGame_ActivePlayer_MatchesFirstPlayer()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("TST-0001", "TST-0002", "TST-0002");

        var (_, state1) = GameInitializer.CreateNewGame("g1", deck, deck, 1, cc);
        state1.ActivePlayer.Should().Be(1);

        var (_, state2) = GameInitializer.CreateNewGame("g2", deck, deck, 2, cc);
        state2.ActivePlayer.Should().Be(2);
    }

    [Fact(DisplayName = "新規ゲームのステータスが Playing になる")]
    public void CreateNewGame_GameStatus_IsPlaying()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("TST-0001", "TST-0002", "TST-0002");

        var (game, _) = GameInitializer.CreateNewGame("g1", deck, deck, 1, cc);

        game.Status.Should().Be(GameStatus.Playing);
    }

    [Fact(DisplayName = "新規ゲームに先攻プレイヤーが設定される")]
    public void CreateNewGame_FirstPlayer_IsSet()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("TST-0001", "TST-0002", "TST-0002");

        var (game, _) = GameInitializer.CreateNewGame("g1", deck, deck, 1, cc);

        game.FirstPlayer.Should().Be(1);
    }

    [Fact(DisplayName = "シャッフルしても手札とデッキにデッキの全カードが保存される")]
    public void CreateNewGame_ShufflePreservesAllCards()
    {
        var cc = SetupCardCache();
        var deckCards = new List<DeckSnapshotCard>();
        for (int i = 0; i < 10; i++)
        {
            deckCards.Add(new DeckSnapshotCard { CardId = "TST-0001" });
            deckCards.Add(new DeckSnapshotCard { CardId = "TEST-0002" });
            deckCards.Add(new DeckSnapshotCard { CardId = "TST-0002" });
        }
        var deck = new DeckSnapshot { DeckID = "d1", Cards = deckCards };

        var (_, state) = GameInitializer.CreateNewGame("g1", deck, deck, 1, cc);

        var allP1CardIds = state.Player1Hand.Select(h => h.CardID)
            .Concat(state.Player1Repository.Select(r => r.CardID))
            .OrderBy(x => x)
            .ToList();

        var expectedSorted = deckCards.Select(c => c.CardId).OrderBy(x => x).ToList();
        allP1CardIds.Should().Equal(expectedSorted);
    }

    [Fact(DisplayName = "配られたカードのインスタンス ID が全て一意になる")]
    public void CreateNewGame_InstanceIDs_AreUnique()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("TST-0001", "TST-0002", "TST-0002");

        var (_, state) = GameInitializer.CreateNewGame("g1", deck, deck, 1, cc);

        var allIds = state.Player1Hand.Select(h => h.InstanceID)
            .Concat(state.Player1Repository.Select(r => r.InstanceID))
            .Concat(state.Player2Hand.Select(h => h.InstanceID))
            .Concat(state.Player2Repository.Select(r => r.InstanceID))
            .ToList();

        allIds.Should().OnlyHaveUniqueItems();
    }

    [Fact(DisplayName = "新規ゲームでは両プレイヤーのタイムバンクが 480 になる")]
    public void CreateNewGame_TimeBank_IsInitialized()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("TST-0001", "TST-0002", "TST-0002");

        var (_, state) = GameInitializer.CreateNewGame("g1", deck, deck, 1, cc);

        state.Player1TimeBank.Should().Be(BattleConstants.InitialTimeBank);
        state.Player2TimeBank.Should().Be(BattleConstants.InitialTimeBank);
        state.Player1TimeBank.Should().Be(480);
    }

    [Fact(DisplayName = "新規ゲームではフィールドの全スロットが空になる")]
    public void CreateNewGame_EmptyField_NoDeployedResources()
    {
        var cc = SetupCardCache();
        var deck = TestFactory.MakeDeck("TST-0001", "TST-0002", "TST-0002");

        var (_, state) = GameInitializer.CreateNewGame("g1", deck, deck, 1, cc);

        state.Player1Field.Frontend.Should().AllSatisfy(slot => slot.Should().BeNull());
        state.Player1Field.Backend.Should().AllSatisfy(slot => slot.Should().BeNull());
        state.Player1Field.Support.Should().AllSatisfy(slot => slot.Should().BeNull());
    }
}
