using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class CardMoveHelpersTests
{
    [Trait("対象", "デッキからのドロー")]
    public class DrawCards
    {
        [Fact(DisplayName = "デッキの上から順に 2 枚を引き、残り 1 枚がデッキに残る")]
        public void DrawsFromFrontOfRepo()
        {
            var state = TestFactory.MakeGameState();
            state.Player1Repository.AddRange(new[]
            {
                new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" },
                new UndeployedCard { InstanceID = "r_2", CardID = "TST-0002" },
                new UndeployedCard { InstanceID = "r_3", CardID = "TST-0003" },
            });

            int drawn = CardMoveHelpers.DrawCards(state, 1, 2);

            drawn.Should().Be(2);
            state.Player1Hand.Should().HaveCount(2);
            state.Player1Hand.Select(h => h.CardID).Should().ContainInOrder("TST-0001", "TST-0002");
            state.Player1Repository.Should().HaveCount(1);
            state.Player1Repository[0].CardID.Should().Be("TST-0003");
        }

        [Fact(DisplayName = "デッキ 1 枚に対し 5 枚要求すると、1 枚だけ引いてデッキは空になる")]
        public void RepoSmallerThanCount_DrawsAll()
        {
            var state = TestFactory.MakeGameState();
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" });

            int drawn = CardMoveHelpers.DrawCards(state, 1, 5);

            drawn.Should().Be(1);
            state.Player1Hand.Should().HaveCount(1);
            state.Player1Repository.Should().BeEmpty();
        }

        [Fact(DisplayName = "デッキが空のとき、0 枚しか引けず手札は空のまま")]
        public void EmptyRepo_DrawsZero()
        {
            var state = TestFactory.MakeGameState();

            int drawn = CardMoveHelpers.DrawCards(state, 1, 3);

            drawn.Should().Be(0);
            state.Player1Hand.Should().BeEmpty();
        }

        [Fact(DisplayName = "引いたカードはデッキで割り当て済みの InstanceID を保持する")]
        public void PreservesDeckInstanceIDs()
        {
            var state = TestFactory.MakeGameState();
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" });
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "r_2", CardID = "TST-0002" });

            CardMoveHelpers.DrawCards(state, 1, 2);

            // 引いたカードはデッキで割り当て済みの InstanceID を保持する (カードの同一性は不変)。
            var ids = state.Player1Hand.Select(h => h.InstanceID).ToList();
            ids.Should().OnlyHaveUniqueItems();
            ids.Should().Contain("r_1");
            ids.Should().Contain("r_2");
        }

        [Fact(DisplayName = "プレイヤー 2 のドローはプレイヤー 2 の状態だけを更新する")]
        public void Player2_UsesPlayer2State()
        {
            var state = TestFactory.MakeGameState();
            state.Player2Repository.Add(new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" });

            int drawn = CardMoveHelpers.DrawCards(state, 2, 1);

            drawn.Should().Be(1);
            state.Player2Hand.Should().HaveCount(1);
            state.Player2Repository.Should().BeEmpty();
            state.Player1Hand.Should().BeEmpty(); // P1 unaffected
        }
    }

    [Trait("対象", "デッキからのカード探索")]
    public class SearchRepo
    {
        [Fact(DisplayName = "条件に一致するカードを見つけて手札へ移し、デッキから取り除く")]
        public void FindsMatchingCard_AddsToHand()
        {
            var state = TestFactory.MakeGameState();
            state.Player1Repository.AddRange(new[]
            {
                new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" },
                new UndeployedCard { InstanceID = "r_2", CardID = "TST-0002" },
                new UndeployedCard { InstanceID = "r_3", CardID = "TST-0003" },
            });

            bool found = CardMoveHelpers.SearchRepo(state, 1, c => c.CardID == "TST-0002");

            found.Should().BeTrue();
            state.Player1Hand.Should().HaveCount(1);
            state.Player1Hand[0].CardID.Should().Be("TST-0002");
            state.Player1Repository.Select(c => c.CardID).Should().NotContain("TST-0002");
        }

        [Fact(DisplayName = "条件に一致するカードが無いとき、false を返し手札もデッキも変わらない")]
        public void NoMatch_ReturnsFalse()
        {
            var state = TestFactory.MakeGameState();
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" });

            bool found = CardMoveHelpers.SearchRepo(state, 1, c => c.CardID == "TEST-0999");

            found.Should().BeFalse();
            state.Player1Hand.Should().BeEmpty();
            state.Player1Repository.Should().HaveCount(1);
        }
    }

    [Trait("対象", "手札への追加")]
    public class AddToHand
    {
        [Fact(DisplayName = "カードを新しい InstanceID で手札に加える")]
        public void AddsCardWithNewInstanceID()
        {
            var state = TestFactory.MakeGameState();

            CardMoveHelpers.AddToHand(state, 1, "TST-0004");

            state.Player1Hand.Should().HaveCount(1);
            state.Player1Hand[0].CardID.Should().Be("TST-0004");
            state.Player1Hand[0].InstanceID.Should().NotBeEmpty();
        }

        [Fact(DisplayName = "複数回追加すると、そのすべてが手札に加わる")]
        public void MultipleCards_AllAdded()
        {
            var state = TestFactory.MakeGameState();

            CardMoveHelpers.AddToHand(state, 1, "TST-0001");
            CardMoveHelpers.AddToHand(state, 1, "TST-0002");

            state.Player1Hand.Should().HaveCount(2);
            state.Player1Hand.Select(h => h.CardID).Should().Contain(new[] { "TST-0001", "TST-0002" });
        }
    }

    [Trait("対象", "トラッシュへの追加")]
    public class AddToTrash
    {
        [Fact(DisplayName = "指定した CardID・InstanceID・ArtNo でトラッシュにカードを加える")]
        public void AddsCardToPlayerTrash()
        {
            var state = TestFactory.MakeGameState();

            CardMoveHelpers.AddToTrash(state, 1, cardID: "TST-0005", instanceID: "inst_50", artNo: 7);

            state.Player1Trash.Should().HaveCount(1);
            state.Player1Trash[0].CardID.Should().Be("TST-0005");
            state.Player1Trash[0].InstanceID.Should().Be("inst_50");
            state.Player1Trash[0].ArtNo.Should().Be(7);
        }
    }

    [Trait("対象", "トラッシュから手札への回収")]
    public class TrashToHand
    {
        [Fact(DisplayName = "トラッシュのカードを手札へ移し、CardID と ArtNo を保ってトラッシュから取り除く")]
        public void MovesCardFromTrashToHand()
        {
            var state = TestFactory.MakeGameState();
            state.Player1Trash.Add(new UndeployedCard { InstanceID = "t_1", CardID = "TST-0006", ArtNo = 3 });

            bool result = CardMoveHelpers.TrashToHand(state, 1, "t_1");

            result.Should().BeTrue();
            state.Player1Trash.Should().BeEmpty();
            state.Player1Hand.Should().HaveCount(1);
            state.Player1Hand[0].CardID.Should().Be("TST-0006");
            state.Player1Hand[0].ArtNo.Should().Be(3);
        }

        [Fact(DisplayName = "指定 InstanceID がトラッシュに無いとき、false を返しトラッシュも手札も変わらない")]
        public void NotFound_ReturnsFalse()
        {
            var state = TestFactory.MakeGameState();
            state.Player1Trash.Add(new UndeployedCard { InstanceID = "t_1", CardID = "TST-0006" });

            bool result = CardMoveHelpers.TrashToHand(state, 1, "nonexistent");

            result.Should().BeFalse();
            state.Player1Trash.Should().HaveCount(1); // unchanged
            state.Player1Hand.Should().BeEmpty();
        }

        [Fact(DisplayName = "回収したカードには新しい InstanceID を割り当てる")]
        public void AssignsNewInstanceID()
        {
            var state = TestFactory.MakeGameState();
            state.Player1Trash.Add(new UndeployedCard { InstanceID = "t_1", CardID = "TST-0006" });

            CardMoveHelpers.TrashToHand(state, 1, "t_1");

            state.Player1Hand[0].InstanceID.Should().NotBe("t_1");
        }
    }

    [Trait("対象", "手札からトラッシュへの破棄")]
    public class DiscardCards
    {
        [Fact(DisplayName = "指定したカードを手札から取り除き、トラッシュへ移す")]
        public void RemovesFromHandAndAddsToTrash()
        {
            var state = TestFactory.MakeGameState();
            state.Player1Hand.AddRange(new[]
            {
                new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" },
                new UndeployedCard { InstanceID = "h_2", CardID = "TST-0002" },
                new UndeployedCard { InstanceID = "h_3", CardID = "TST-0003" },
            });

            int discarded = CardMoveHelpers.DiscardCards(state, 1, ["h_1", "h_3"]);

            discarded.Should().Be(2);
            state.Player1Hand.Should().HaveCount(1);
            state.Player1Hand[0].InstanceID.Should().Be("h_2");
            state.Player1Trash.Select(c => c.CardID).Should().Contain(new[] { "TST-0001", "TST-0003" });
        }

        [Fact(DisplayName = "手札に無いカードを指定すると、GameRuleException を投げる")]
        public void CardNotInHand_Throws()
        {
            var state = TestFactory.MakeGameState();
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" });

            var act = () => CardMoveHelpers.DiscardCards(state, 1, ["h_1", "h_missing"]);

            act.Should().Throw<GameRuleException>();
        }

        [Fact(DisplayName = "空リストを渡すと、何も破棄せず手札もトラッシュも変わらない")]
        public void EmptyList_DiscardsNothing()
        {
            var state = TestFactory.MakeGameState();
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" });

            int discarded = CardMoveHelpers.DiscardCards(state, 1, []);

            discarded.Should().Be(0);
            state.Player1Hand.Should().HaveCount(1);
            state.Player1Trash.Should().BeEmpty();
        }

        [Fact(DisplayName = "手札の全カードを破棄すると、手札が空になりトラッシュへ移る")]
        public void AllCards_HandBecomesEmpty()
        {
            var state = TestFactory.MakeGameState();
            state.Player1Hand.AddRange(new[]
            {
                new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" },
                new UndeployedCard { InstanceID = "h_2", CardID = "TST-0002" },
            });

            int discarded = CardMoveHelpers.DiscardCards(state, 1, ["h_1", "h_2"]);

            discarded.Should().Be(2);
            state.Player1Hand.Should().BeEmpty();
            state.Player1Trash.Should().HaveCount(2);
        }
    }
}
