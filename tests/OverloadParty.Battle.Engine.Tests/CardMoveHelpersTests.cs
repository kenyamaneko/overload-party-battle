using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// Tests for CardMoveHelpers: drawing cards, searching repo,
/// adding to hand/trash, discarding, and trash-to-hand recovery.
/// </summary>
public class CardMoveHelpersTests
{
    // ─── DrawCards ─────────────────────────────────────────────

    [Fact]
    public void DrawCards_DrawsFromFrontOfRepo()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Repository.AddRange(new[]
        {
            new UndeployedCard { InstanceID = "r_1", CardID = "TST-2009" },
            new UndeployedCard { InstanceID = "r_2", CardID = "TST-2019" },
            new UndeployedCard { InstanceID = "r_3", CardID = "TST-4008" },
        });

        int drawn = CardMoveHelpers.DrawCards(state, 1, 2);

        drawn.Should().Be(2);
        state.Player1Hand.Should().HaveCount(2);
        state.Player1Hand.Select(h => h.CardID).Should().ContainInOrder("TST-2009", "TST-2019");
        state.Player1Repository.Should().HaveCount(1);
        state.Player1Repository[0].CardID.Should().Be("TST-4008");
    }

    [Fact]
    public void DrawCards_RepoSmallerThanCount_DrawsAll()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Repository.Add(new UndeployedCard { InstanceID = "r_1", CardID = "TST-2009" });

        int drawn = CardMoveHelpers.DrawCards(state, 1, 5);

        drawn.Should().Be(1);
        state.Player1Hand.Should().HaveCount(1);
        state.Player1Repository.Should().BeEmpty();
    }

    [Fact]
    public void DrawCards_EmptyRepo_DrawsZero()
    {
        var state = TestFactory.MakeGameState();

        int drawn = CardMoveHelpers.DrawCards(state, 1, 3);

        drawn.Should().Be(0);
        state.Player1Hand.Should().BeEmpty();
    }

    [Fact]
    public void DrawCards_AssignsNewInstanceIDs()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Repository.Add(new UndeployedCard { InstanceID = "r_1", CardID = "TST-2009" });
        state.Player1Repository.Add(new UndeployedCard { InstanceID = "r_2", CardID = "TST-2019" });

        CardMoveHelpers.DrawCards(state, 1, 2);

        // Each drawn card should have a unique new InstanceID
        var ids = state.Player1Hand.Select(h => h.InstanceID).ToList();
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().NotContain("r_1");
        ids.Should().NotContain("r_2");
    }

    [Fact]
    public void DrawCards_Player2_UsesPlayer2State()
    {
        var state = TestFactory.MakeGameState();
        state.Player2Repository.Add(new UndeployedCard { InstanceID = "r_1", CardID = "TST-2009" });

        int drawn = CardMoveHelpers.DrawCards(state, 2, 1);

        drawn.Should().Be(1);
        state.Player2Hand.Should().HaveCount(1);
        state.Player2Repository.Should().BeEmpty();
        state.Player1Hand.Should().BeEmpty(); // P1 unaffected
    }

    // ─── SearchRepo ───────────────────────────────────────────

    [Fact]
    public void SearchRepo_FindsMatchingCard_AddsToHand()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Repository.AddRange(new[]
        {
            new UndeployedCard { InstanceID = "r_1", CardID = "TST-2009" },
            new UndeployedCard { InstanceID = "r_2", CardID = "TST-2019" },
            new UndeployedCard { InstanceID = "r_3", CardID = "TST-4008" },
        });

        bool found = CardMoveHelpers.SearchRepo(state, 1, c => c.CardID == "TST-2019");

        found.Should().BeTrue();
        state.Player1Hand.Should().HaveCount(1);
        state.Player1Hand[0].CardID.Should().Be("TST-2019");
        state.Player1Repository.Select(c => c.CardID).Should().NotContain("TST-2019");
    }

    [Fact]
    public void SearchRepo_NoMatch_ReturnsFalse()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Repository.Add(new UndeployedCard { InstanceID = "r_1", CardID = "TST-2009" });

        bool found = CardMoveHelpers.SearchRepo(state, 1, c => c.CardID == "TEST-0999");

        found.Should().BeFalse();
        state.Player1Hand.Should().BeEmpty();
        state.Player1Repository.Should().HaveCount(1);
    }

    // ─── AddToHand ────────────────────────────────────────────

    [Fact]
    public void AddToHand_AddsCardWithNewInstanceID()
    {
        var state = TestFactory.MakeGameState();

        CardMoveHelpers.AddToHand(state, 1, "TST-4020");

        state.Player1Hand.Should().HaveCount(1);
        state.Player1Hand[0].CardID.Should().Be("TST-4020");
        state.Player1Hand[0].InstanceID.Should().NotBeEmpty();
    }

    [Fact]
    public void AddToHand_MultipleCards_AllAdded()
    {
        var state = TestFactory.MakeGameState();

        CardMoveHelpers.AddToHand(state, 1, "TST-2009");
        CardMoveHelpers.AddToHand(state, 1, "TST-2019");

        state.Player1Hand.Should().HaveCount(2);
        state.Player1Hand.Select(h => h.CardID).Should().Contain(new[] { "TST-2009", "TST-2019" });
    }

    // ─── AddToTrash ───────────────────────────────────────────

    [Fact]
    public void AddToTrash_AddsCardToPlayerTrash()
    {
        var state = TestFactory.MakeGameState();

        CardMoveHelpers.AddToTrash(state, 1, cardID: "TST-3004", instanceID: "inst_50", artNo: 7);

        state.Player1Trash.Should().HaveCount(1);
        state.Player1Trash[0].CardID.Should().Be("TST-3004");
        state.Player1Trash[0].InstanceID.Should().Be("inst_50");
        state.Player1Trash[0].ArtNo.Should().Be(7);
    }

    // ─── TrashToHand ──────────────────────────────────────────

    [Fact]
    public void TrashToHand_MovesCardFromTrashToHand()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Trash.Add(new UndeployedCard { InstanceID = "t_1", CardID = "TST-3015", ArtNo = 3 });

        bool result = CardMoveHelpers.TrashToHand(state, 1, "t_1");

        result.Should().BeTrue();
        state.Player1Trash.Should().BeEmpty();
        state.Player1Hand.Should().HaveCount(1);
        state.Player1Hand[0].CardID.Should().Be("TST-3015");
        state.Player1Hand[0].ArtNo.Should().Be(3);
    }

    [Fact]
    public void TrashToHand_NotFound_ReturnsFalse()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Trash.Add(new UndeployedCard { InstanceID = "t_1", CardID = "TST-3015" });

        bool result = CardMoveHelpers.TrashToHand(state, 1, "nonexistent");

        result.Should().BeFalse();
        state.Player1Trash.Should().HaveCount(1); // unchanged
        state.Player1Hand.Should().BeEmpty();
    }

    [Fact]
    public void TrashToHand_AssignsNewInstanceID()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Trash.Add(new UndeployedCard { InstanceID = "t_1", CardID = "TST-3015" });

        CardMoveHelpers.TrashToHand(state, 1, "t_1");

        state.Player1Hand[0].InstanceID.Should().NotBe("t_1");
    }

    // ─── DiscardCards ─────────────────────────────────────────

    [Fact]
    public void DiscardCards_RemovesFromHandAndAddsToTrash()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Hand.AddRange(new[]
        {
            new UndeployedCard { InstanceID = "h_1", CardID = "TST-2009" },
            new UndeployedCard { InstanceID = "h_2", CardID = "TST-2019" },
            new UndeployedCard { InstanceID = "h_3", CardID = "TST-4008" },
        });

        int discarded = CardMoveHelpers.DiscardCards(state, 1, ["h_1", "h_3"]);

        discarded.Should().Be(2);
        state.Player1Hand.Should().HaveCount(1);
        state.Player1Hand[0].InstanceID.Should().Be("h_2");
        state.Player1Trash.Select(c => c.CardID).Should().Contain(new[] { "TST-2009", "TST-4008" });
    }

    [Fact]
    public void DiscardCards_CardNotInHand_Throws()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TST-2009" });

        var act = () => CardMoveHelpers.DiscardCards(state, 1, ["h_1", "h_missing"]);

        act.Should().Throw<GameRuleException>();
    }

    [Fact]
    public void DiscardCards_EmptyList_DiscardsNothing()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TST-2009" });

        int discarded = CardMoveHelpers.DiscardCards(state, 1, []);

        discarded.Should().Be(0);
        state.Player1Hand.Should().HaveCount(1);
        state.Player1Trash.Should().BeEmpty();
    }

    [Fact]
    public void DiscardCards_AllCards_HandBecomesEmpty()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Hand.AddRange(new[]
        {
            new UndeployedCard { InstanceID = "h_1", CardID = "TST-2009" },
            new UndeployedCard { InstanceID = "h_2", CardID = "TST-2019" },
        });

        int discarded = CardMoveHelpers.DiscardCards(state, 1, ["h_1", "h_2"]);

        discarded.Should().Be(2);
        state.Player1Hand.Should().BeEmpty();
        state.Player1Trash.Should().HaveCount(2);
    }
}
