using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class GameInitializerTests
{
    /// <summary>デッキに入れる 3 種のカードを登録したキャッシュを作る。</summary>
    /// <returns>TST-0001〜TST-0003 を登録したキャッシュ。</returns>
    private static TestCardCache MakeCache()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", name: "Card1"));
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0002", name: "Card2"));
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0003", name: "Card3"));
        return cc;
    }

    /// <summary>デッキ規約を満たす 30 枚のデッキを組む。</summary>
    /// <param name="cc">埋め草カードの登録先。</param>
    /// <returns>30 枚のデッキスナップショット。</returns>
    private static DeckSnapshot MakeValidDeck(TestCardCache cc) =>
        TestFactory.MakeDeck(cc, "TST-0001", "TST-0002", "TST-0003");

    /// <summary>同名上限を守ったまま指定枚数のデッキを組む。</summary>
    /// <param name="cc">カードの登録先。</param>
    /// <param name="count">デッキの枚数。</param>
    /// <returns>指定枚数のデッキスナップショット。</returns>
    private static DeckSnapshot MakeDeckOfSize(TestCardCache cc, int count)
    {
        var cards = new List<DeckSnapshotCard>();
        int cardNo = 1;
        while (cards.Count < count)
        {
            var cardId = $"TST-1{cardNo:D3}";
            cc.Add(TestFactory.ComputeCard(cardId: cardId, name: $"Sized{cardNo}"));
            int copies = Math.Min(BattleConstants.MaxCopiesPerCardName, count - cards.Count);
            for (int i = 0; i < copies; i++)
            {
                cards.Add(new DeckSnapshotCard { CardId = cardId });
            }
            cardNo++;
        }
        return new DeckSnapshot { DeckID = "d1", Cards = cards };
    }

    /// <summary>ゲームを作成する。</summary>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="deck">両プレイヤーが使うデッキ。</param>
    /// <param name="firstPlayer">先攻プレイヤー番号。</param>
    /// <returns>作成された Game とゲーム状態。</returns>
    private static (Game Game, BattleGameState State) CreateGame(
        TestCardCache cc, DeckSnapshot deck, long firstPlayer = 1) =>
        GameInitializer.CreateNewGame("g1", deck, deck, firstPlayer, cc, new FakeClock());

    [Trait("対象", "新規ゲームの初期リソース")]
    public class InitialResources
    {
        [Fact(DisplayName = "新規ゲームでは両プレイヤーのバジェットが 5000 になる")]
        public void Budget_Is5000()
        {
            var cc = MakeCache();

            var (_, state) = CreateGame(cc, MakeValidDeck(cc));

            state.Player1Budget.Should().Be(5000);
            state.Player2Budget.Should().Be(5000);
        }

        [Fact(DisplayName = "新規ゲームでは両プレイヤーのインサイトプールが 0 になる")]
        public void InsightPool_IsZero()
        {
            var cc = MakeCache();

            var (_, state) = CreateGame(cc, MakeValidDeck(cc));

            state.Player1InsightPool.Should().Be(0);
            state.Player2InsightPool.Should().Be(0);
        }

        [Fact(DisplayName = "新規ゲームでは両プレイヤーのタイムバンクが 480 になる")]
        public void TimeBank_Is480()
        {
            var cc = MakeCache();

            var (_, state) = CreateGame(cc, MakeValidDeck(cc));

            state.Player1TimeBank.Should().Be(480);
            state.Player2TimeBank.Should().Be(480);
        }
    }

    [Trait("対象", "新規ゲームの手札とデッキの配り分け")]
    public class DealingCards
    {
        [Fact(DisplayName = "新規ゲームでは各プレイヤーに手札が 5 枚配られる")]
        public void Deals5Cards_PerPlayer()
        {
            var cc = MakeCache();

            var (_, state) = CreateGame(cc, MakeValidDeck(cc));

            state.Player1Hand.Should().HaveCount(5);
            state.Player2Hand.Should().HaveCount(5);
        }

        [Fact(DisplayName = "新規ゲームでは手札 5 枚を除いた 25 枚がデッキに残る")]
        public void RemainingCards_GoToRepository()
        {
            var cc = MakeCache();

            var (_, state) = CreateGame(cc, MakeValidDeck(cc));

            state.Player1Repository.Should().HaveCount(25);
            state.Player2Repository.Should().HaveCount(25);
        }

        [Fact(DisplayName = "シャッフルしても手札とデッキにデッキの全カードが保存される")]
        public void ShufflePreservesAllCards()
        {
            var cc = MakeCache();
            var deck = MakeValidDeck(cc);

            var (_, state) = CreateGame(cc, deck);

            var dealtCardIds = state.Player1Hand.Select(h => h.CardID)
                .Concat(state.Player1Repository.Select(r => r.CardID))
                .OrderBy(x => x)
                .ToList();

            dealtCardIds.Should().Equal(deck.Cards.Select(c => c.CardId).OrderBy(x => x));
        }

        [Fact(DisplayName = "配られたカードのインスタンス ID が全て一意になる")]
        public void InstanceIDs_AreUnique()
        {
            var cc = MakeCache();

            var (_, state) = CreateGame(cc, MakeValidDeck(cc));

            var allIds = state.Player1Hand.Select(h => h.InstanceID)
                .Concat(state.Player1Repository.Select(r => r.InstanceID))
                .Concat(state.Player2Hand.Select(h => h.InstanceID))
                .Concat(state.Player2Repository.Select(r => r.InstanceID))
                .ToList();

            allIds.Should().OnlyHaveUniqueItems();
        }
    }

    [Trait("対象", "新規ゲームの開始状態")]
    public class StartingState
    {
        [Fact(DisplayName = "新規ゲームはターン 1 のドローフェーズで始まる")]
        public void StartsAtTurn1_DrawPhase()
        {
            var cc = MakeCache();

            var (_, state) = CreateGame(cc, MakeValidDeck(cc));

            state.CurrentTurn.Should().Be(1);
            state.CurrentPhase.Should().Be(Phase.Draw);
        }

        [Fact(DisplayName = "先攻をプレイヤー 1 にするとターンプレイヤーが 1 になる")]
        public void FirstPlayer1_ActivePlayerIs1()
        {
            var cc = MakeCache();

            var (game, state) = CreateGame(cc, MakeValidDeck(cc), firstPlayer: 1);

            state.ActivePlayer.Should().Be(1);
            game.FirstPlayer.Should().Be(1);
        }

        [Fact(DisplayName = "先攻をプレイヤー 2 にするとターンプレイヤーが 2 になる")]
        public void FirstPlayer2_ActivePlayerIs2()
        {
            var cc = MakeCache();

            var (game, state) = CreateGame(cc, MakeValidDeck(cc), firstPlayer: 2);

            state.ActivePlayer.Should().Be(2);
            game.FirstPlayer.Should().Be(2);
        }

        [Fact(DisplayName = "新規ゲームのステータスが Playing になる")]
        public void GameStatus_IsPlaying()
        {
            var cc = MakeCache();

            var (game, _) = CreateGame(cc, MakeValidDeck(cc));

            game.Status.Should().Be(GameStatus.Playing);
        }

        [Fact(DisplayName = "新規ゲームではフィールドの全スロットが空になる")]
        public void EmptyField_NoDeployedResources()
        {
            var cc = MakeCache();

            var (_, state) = CreateGame(cc, MakeValidDeck(cc));

            state.Player1Field.Frontend.Should().AllSatisfy(slot => slot.Should().BeNull());
            state.Player1Field.Backend.Should().AllSatisfy(slot => slot.Should().BeNull());
            state.Player1Field.Support.Should().AllSatisfy(slot => slot.Should().BeNull());
        }
    }

    [Trait("対象", "ゲーム開始時のデッキ構成の検証")]
    public class DeckValidation
    {
        [Fact(DisplayName = "デッキが 30 枚のとき、ゲームを開始できる")]
        public void ExactDeckSize_Starts()
        {
            var cc = new TestCardCache();
            var deck = MakeDeckOfSize(cc, 30);

            var (game, _) = CreateGame(cc, deck);

            game.Status.Should().Be(GameStatus.Playing);
        }

        [Fact(DisplayName = "デッキが 29 枚のとき、枚数を理由に開始できない")]
        public void TooFewCards_Throws()
        {
            var cc = new TestCardCache();
            var deck = MakeDeckOfSize(cc, 29);

            var act = () => CreateGame(cc, deck);

            act.Should().Throw<GameRuleException>().WithMessage("*has 29 cards, must be exactly 30*");
        }

        [Fact(DisplayName = "デッキが 31 枚のとき、枚数を理由に開始できない")]
        public void TooManyCards_Throws()
        {
            var cc = new TestCardCache();
            var deck = MakeDeckOfSize(cc, 31);

            var act = () => CreateGame(cc, deck);

            act.Should().Throw<GameRuleException>().WithMessage("*has 31 cards, must be exactly 30*");
        }

        [Fact(DisplayName = "同名カードが 3 枚までのとき、ゲームを開始できる")]
        public void ThreeCopies_Starts()
        {
            var cc = MakeCache();

            var (game, _) = CreateGame(cc, MakeValidDeck(cc));

            game.Status.Should().Be(GameStatus.Playing);
        }

        [Fact(DisplayName = "同名カードを 4 枚含むとき、同名上限を理由に開始できない")]
        public void FourCopies_Throws()
        {
            var cc = new TestCardCache();
            var deck = MakeDeckOfSize(cc, 30);
            var overCopiedId = deck.Cards[0].CardId;
            deck.Cards[^1].CardId = overCopiedId;

            var act = () => CreateGame(cc, deck);

            act.Should().Throw<GameRuleException>()
                .WithMessage($"*exceeds the 3 copy limit: {overCopiedId} x4*");
        }

        [Fact(DisplayName = "未登録カードを含むとき、そのカード ID を理由に開始できない")]
        public void UnregisteredCard_Throws()
        {
            var cc = new TestCardCache();
            var deck = MakeDeckOfSize(cc, 30);
            deck.Cards[0].CardId = "TST-9999";

            var act = () => CreateGame(cc, deck);

            act.Should().Throw<GameRuleException>().WithMessage("*unknown card_id(s): TST-9999*");
        }
    }
}
