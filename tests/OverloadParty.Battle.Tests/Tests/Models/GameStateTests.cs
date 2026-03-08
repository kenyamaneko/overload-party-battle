using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Models;

/// <summary>
/// Tests for GameState accessor helpers. Validates that playerNum-based
/// getters/setters correctly route to the underlying Player1/Player2 properties.
/// </summary>
public class GameStateTests
{
    // ─── GetField / SetField ───────────────────────────────────

    [Fact]
    public void GetField_Player1_ReturnsPlayer1Field()
    {
        var gs = TestFactory.MakeGameState();
        gs.GetField(1).Should().BeSameAs(gs.Player1Field);
    }

    [Fact]
    public void GetField_Player2_ReturnsPlayer2Field()
    {
        var gs = TestFactory.MakeGameState();
        gs.GetField(2).Should().BeSameAs(gs.Player2Field);
    }

    [Fact]
    public void GetField_InvalidPlayer_Throws()
    {
        var gs = TestFactory.MakeGameState();
        var act = () => gs.GetField(3);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetField_Player1_UpdatesPlayer1Field()
    {
        var gs = TestFactory.MakeGameState();
        var newField = new Field();
        gs.SetField(1, newField);
        gs.Player1Field.Should().BeSameAs(newField);
    }

    [Fact]
    public void SetField_Player2_UpdatesPlayer2Field()
    {
        var gs = TestFactory.MakeGameState();
        var newField = new Field();
        gs.SetField(2, newField);
        gs.Player2Field.Should().BeSameAs(newField);
    }

    [Fact]
    public void SetField_InvalidPlayer_Throws()
    {
        var gs = TestFactory.MakeGameState();
        var act = () => gs.SetField(0, new Field());
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ─── GetHand / SetHand ─────────────────────────────────────

    [Fact]
    public void GetHand_Player1_ReturnsPlayer1Hand()
    {
        var gs = TestFactory.MakeGameState();
        gs.GetHand(1).Should().BeSameAs(gs.Player1Hand);
    }

    [Fact]
    public void GetHand_Player2_ReturnsPlayer2Hand()
    {
        var gs = TestFactory.MakeGameState();
        gs.GetHand(2).Should().BeSameAs(gs.Player2Hand);
    }

    [Fact]
    public void GetHand_InvalidPlayer_Throws()
    {
        var gs = TestFactory.MakeGameState();
        var act = () => gs.GetHand(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetHand_Player1_UpdatesPlayer1Hand()
    {
        var gs = TestFactory.MakeGameState();
        var newHand = new List<HandCard> { new() { InstanceID = "h1" } };
        gs.SetHand(1, newHand);
        gs.Player1Hand.Should().BeSameAs(newHand);
    }

    [Fact]
    public void SetHand_Player2_UpdatesPlayer2Hand()
    {
        var gs = TestFactory.MakeGameState();
        var newHand = new List<HandCard> { new() { InstanceID = "h2" } };
        gs.SetHand(2, newHand);
        gs.Player2Hand.Should().BeSameAs(newHand);
    }

    [Fact]
    public void SetHand_InvalidPlayer_Throws()
    {
        var gs = TestFactory.MakeGameState();
        var act = () => gs.SetHand(3, []);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ─── GetBudget / SetBudget ─────────────────────────────────

    [Theory]
    [InlineData(1, 5000)]
    [InlineData(2, 5000)]
    public void GetBudget_ReturnsCorrectPlayerBudget(long playerNum, long expected)
    {
        var gs = TestFactory.MakeGameState(p1Budget: 5000, p2Budget: 5000);
        gs.GetBudget(playerNum).Should().Be(expected);
    }

    [Fact]
    public void GetBudget_InvalidPlayer_Throws()
    {
        var gs = TestFactory.MakeGameState();
        var act = () => gs.GetBudget(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetBudget_Player1_Updates()
    {
        var gs = TestFactory.MakeGameState();
        gs.SetBudget(1, 3000);
        gs.Player1Budget.Should().Be(3000);
    }

    [Fact]
    public void SetBudget_Player2_Updates()
    {
        var gs = TestFactory.MakeGameState();
        gs.SetBudget(2, 4000);
        gs.Player2Budget.Should().Be(4000);
    }

    [Fact]
    public void SetBudget_InvalidPlayer_Throws()
    {
        var gs = TestFactory.MakeGameState();
        var act = () => gs.SetBudget(3, 100);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ─── GetInsightPool / SetInsightPool ───────────────────────

    [Fact]
    public void GetInsightPool_Player1_ReturnsValue()
    {
        var gs = TestFactory.MakeGameState();
        gs.Player1InsightPool = 10;
        gs.GetInsightPool(1).Should().Be(10);
    }

    [Fact]
    public void GetInsightPool_Player2_ReturnsValue()
    {
        var gs = TestFactory.MakeGameState();
        gs.Player2InsightPool = 20;
        gs.GetInsightPool(2).Should().Be(20);
    }

    [Fact]
    public void GetInsightPool_InvalidPlayer_Throws()
    {
        var gs = TestFactory.MakeGameState();
        var act = () => gs.GetInsightPool(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetInsightPool_Player1_Updates()
    {
        var gs = TestFactory.MakeGameState();
        gs.SetInsightPool(1, 15);
        gs.Player1InsightPool.Should().Be(15);
    }

    [Fact]
    public void SetInsightPool_Player2_Updates()
    {
        var gs = TestFactory.MakeGameState();
        gs.SetInsightPool(2, 25);
        gs.Player2InsightPool.Should().Be(25);
    }

    [Fact]
    public void SetInsightPool_InvalidPlayer_Throws()
    {
        var gs = TestFactory.MakeGameState();
        var act = () => gs.SetInsightPool(3, 5);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ─── GetRepository / SetRepository ─────────────────────────

    [Fact]
    public void GetRepository_Player1_ReturnsPlayer1Repository()
    {
        var gs = TestFactory.MakeGameState();
        gs.GetRepository(1).Should().BeSameAs(gs.Player1Repository);
    }

    [Fact]
    public void GetRepository_Player2_ReturnsPlayer2Repository()
    {
        var gs = TestFactory.MakeGameState();
        gs.GetRepository(2).Should().BeSameAs(gs.Player2Repository);
    }

    [Fact]
    public void GetRepository_InvalidPlayer_Throws()
    {
        var gs = TestFactory.MakeGameState();
        var act = () => gs.GetRepository(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetRepository_Player1_Updates()
    {
        var gs = TestFactory.MakeGameState();
        var repo = new List<HandCard> { new() { InstanceID = "r1" } };
        gs.SetRepository(1, repo);
        gs.Player1Repository.Should().BeSameAs(repo);
    }

    [Fact]
    public void SetRepository_Player2_Updates()
    {
        var gs = TestFactory.MakeGameState();
        var repo = new List<HandCard> { new() { InstanceID = "r2" } };
        gs.SetRepository(2, repo);
        gs.Player2Repository.Should().BeSameAs(repo);
    }

    [Fact]
    public void SetRepository_InvalidPlayer_Throws()
    {
        var gs = TestFactory.MakeGameState();
        var act = () => gs.SetRepository(3, []);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ─── GetTrash / SetTrash ───────────────────────────────────

    [Fact]
    public void GetTrash_Player1_ReturnsPlayer1Trash()
    {
        var gs = TestFactory.MakeGameState();
        gs.GetTrash(1).Should().BeSameAs(gs.Player1Trash);
    }

    [Fact]
    public void GetTrash_Player2_ReturnsPlayer2Trash()
    {
        var gs = TestFactory.MakeGameState();
        gs.GetTrash(2).Should().BeSameAs(gs.Player2Trash);
    }

    [Fact]
    public void GetTrash_InvalidPlayer_Throws()
    {
        var gs = TestFactory.MakeGameState();
        var act = () => gs.GetTrash(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetTrash_Player1_Updates()
    {
        var gs = TestFactory.MakeGameState();
        var trash = new List<HandCard> { new() { InstanceID = "t1" } };
        gs.SetTrash(1, trash);
        gs.Player1Trash.Should().BeSameAs(trash);
    }

    [Fact]
    public void SetTrash_Player2_Updates()
    {
        var gs = TestFactory.MakeGameState();
        var trash = new List<HandCard> { new() { InstanceID = "t2" } };
        gs.SetTrash(2, trash);
        gs.Player2Trash.Should().BeSameAs(trash);
    }

    [Fact]
    public void SetTrash_InvalidPlayer_Throws()
    {
        var gs = TestFactory.MakeGameState();
        var act = () => gs.SetTrash(3, []);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ─── GetTimeBank / SetTimeBank ─────────────────────────────

    [Fact]
    public void GetTimeBank_Player1_ReturnsValue()
    {
        var gs = TestFactory.MakeGameState();
        gs.GetTimeBank(1).Should().Be(480);
    }

    [Fact]
    public void GetTimeBank_Player2_ReturnsValue()
    {
        var gs = TestFactory.MakeGameState();
        gs.GetTimeBank(2).Should().Be(480);
    }

    [Fact]
    public void GetTimeBank_InvalidPlayer_Throws()
    {
        var gs = TestFactory.MakeGameState();
        var act = () => gs.GetTimeBank(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetTimeBank_Player1_Updates()
    {
        var gs = TestFactory.MakeGameState();
        gs.SetTimeBank(1, 300);
        gs.Player1TimeBank.Should().Be(300);
    }

    [Fact]
    public void SetTimeBank_Player2_Updates()
    {
        var gs = TestFactory.MakeGameState();
        gs.SetTimeBank(2, 200);
        gs.Player2TimeBank.Should().Be(200);
    }

    [Fact]
    public void SetTimeBank_InvalidPlayer_Throws()
    {
        var gs = TestFactory.MakeGameState();
        var act = () => gs.SetTimeBank(3, 100);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ─── GetIncidentPlayedThisTurn / SetIncidentPlayedThisTurn ─

    [Fact]
    public void GetIncidentPlayedThisTurn_DefaultsFalse()
    {
        var gs = TestFactory.MakeGameState();
        gs.GetIncidentPlayedThisTurn(1).Should().BeFalse();
        gs.GetIncidentPlayedThisTurn(2).Should().BeFalse();
    }

    [Fact]
    public void SetIncidentPlayedThisTurn_Player1_Updates()
    {
        var gs = TestFactory.MakeGameState();
        gs.SetIncidentPlayedThisTurn(1, true);
        gs.Player1IncidentPlayedThisTurn.Should().BeTrue();
    }

    [Fact]
    public void SetIncidentPlayedThisTurn_Player2_Updates()
    {
        var gs = TestFactory.MakeGameState();
        gs.SetIncidentPlayedThisTurn(2, true);
        gs.Player2IncidentPlayedThisTurn.Should().BeTrue();
    }

    [Fact]
    public void GetIncidentPlayedThisTurn_InvalidPlayer_Throws()
    {
        var gs = TestFactory.MakeGameState();
        var act = () => gs.GetIncidentPlayedThisTurn(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetIncidentPlayedThisTurn_InvalidPlayer_Throws()
    {
        var gs = TestFactory.MakeGameState();
        var act = () => gs.SetIncidentPlayedThisTurn(3, true);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ─── GetHasHadActiveResource / SetHasHadActiveResource ─────

    [Fact]
    public void GetHasHadActiveResource_DefaultsFalse()
    {
        var gs = TestFactory.MakeGameState();
        gs.GetHasHadActiveResource(1).Should().BeFalse();
        gs.GetHasHadActiveResource(2).Should().BeFalse();
    }

    [Fact]
    public void SetHasHadActiveResource_Player1_Updates()
    {
        var gs = TestFactory.MakeGameState();
        gs.SetHasHadActiveResource(1, true);
        gs.Player1HasHadActiveResource.Should().BeTrue();
    }

    [Fact]
    public void SetHasHadActiveResource_Player2_Updates()
    {
        var gs = TestFactory.MakeGameState();
        gs.SetHasHadActiveResource(2, true);
        gs.Player2HasHadActiveResource.Should().BeTrue();
    }

    [Fact]
    public void GetHasHadActiveResource_InvalidPlayer_Throws()
    {
        var gs = TestFactory.MakeGameState();
        var act = () => gs.GetHasHadActiveResource(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetHasHadActiveResource_InvalidPlayer_Throws()
    {
        var gs = TestFactory.MakeGameState();
        var act = () => gs.SetHasHadActiveResource(3, true);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ─── OpponentOf ────────────────────────────────────────────

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public void OpponentOf_ReturnsOtherPlayer(long playerNum, long expected)
    {
        var gs = TestFactory.MakeGameState();
        gs.OpponentOf(playerNum).Should().Be(expected);
    }

    [Fact]
    public void OpponentOf_InvalidPlayer_Throws()
    {
        var gs = TestFactory.MakeGameState();
        var act = () => gs.OpponentOf(3);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ─── NextInstanceID ────────────────────────────────────────

    [Fact]
    public void NextInstanceID_ReturnsSequentialIDs()
    {
        var gs = TestFactory.MakeGameState();
        gs.NextInstanceSeq = 0;

        gs.NextInstanceID().Should().Be("inst_0");
        gs.NextInstanceID().Should().Be("inst_1");
        gs.NextInstanceID().Should().Be("inst_2");
        gs.NextInstanceSeq.Should().Be(3);
    }

    // ─── NextDeployOrder ───────────────────────────────────────

    [Fact]
    public void NextDeployOrder_ReturnsIncreasingValues()
    {
        var gs = TestFactory.MakeGameState();
        gs.NextDeployOrderSeq = 0;

        gs.NextDeployOrder().Should().Be(1);
        gs.NextDeployOrder().Should().Be(2);
        gs.NextDeployOrder().Should().Be(3);
        gs.NextDeployOrderSeq.Should().Be(3);
    }

    // ─── Game.GetPlayerID ──────────────────────────────────────

    [Fact]
    public void Game_GetPlayerID_Player1_ReturnsPlayer1ID()
    {
        var game = TestFactory.MakeGame("alice", "bob");
        game.GetPlayerID(1).Should().Be("alice");
    }

    [Fact]
    public void Game_GetPlayerID_Player2_ReturnsPlayer2ID()
    {
        var game = TestFactory.MakeGame("alice", "bob");
        game.GetPlayerID(2).Should().Be("bob");
    }

    [Fact]
    public void Game_GetPlayerID_InvalidPlayer_Throws()
    {
        var game = TestFactory.MakeGame();
        var act = () => game.GetPlayerID(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
